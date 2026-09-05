using System.Collections.Concurrent;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class RemoteOperationDispatcher
{
    private readonly IReadOnlyDictionary<NetworkOperationType, IRemoteOperationHandler> _handlers;
    private readonly RemoteOperationOptions _options;
    private readonly ISystemClock _clock;
    private readonly ILicenseStateProvider? _licenseStateProvider;
    private readonly RemoteOperationLicensePolicy _licensePolicy;
    private readonly ConcurrentDictionary<string, OperationState> _operations = new(StringComparer.Ordinal);
    private int _dispatchCount;

    public RemoteOperationDispatcher(
        IEnumerable<IRemoteOperationHandler> handlers,
        RemoteOperationOptions options,
        ISystemClock clock,
        ILicenseStateProvider? licenseStateProvider = null,
        RemoteOperationLicensePolicy? licensePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _handlers = handlers
            .GroupBy(handler => handler.OperationType)
            .ToDictionary(group => group.Key, group => group.First());
        _options = options;
        _clock = clock;
        _licenseStateProvider = licenseStateProvider;
        _licensePolicy = licensePolicy ?? RemoteOperationLicensePolicy.Default;
    }

    public async Task<RemoteOperationDispatchResult> DispatchAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var signature = RequestSignature.From(request);
        var state = new OperationState(
            signature,
            new Lazy<Task<OperationResult>>(
                () => ExecuteOnceAsync(request, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var existing = _operations.GetOrAdd(request.OperationId ?? string.Empty, state);
        var duplicate = !ReferenceEquals(existing, state);

        OperationResult result;
        if (duplicate && !SameRequest(existing.Signature, signature))
        {
            result = DuplicateConflict(request);
        }
        else
        {
            result = await existing.Result.Value.ConfigureAwait(false);
            existing.MarkCompleted(result.CompletedAtUnixMs);
        }

        MaybeCleanupCompletedOperations();

        return new RemoteOperationDispatchResult(
            Accepted(request),
            result,
            duplicate);
    }

    public OperationResult? TryGetCompletedResult(string operationId, string targetDeviceId)
    {
        if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(targetDeviceId))
        {
            return null;
        }

        if (!_operations.TryGetValue(operationId, out var state)
            || !string.Equals(state.Signature.TargetDeviceId, targetDeviceId, StringComparison.Ordinal)
            || !state.Result.IsValueCreated
            || !state.Result.Value.IsCompletedSuccessfully)
        {
            return null;
        }

        OperationResult result = state.Result.Value.Result;
        if (!string.Equals(result.TargetDeviceId, targetDeviceId, StringComparison.Ordinal))
        {
            return null;
        }

        var completedAtUtc = state.CompletedAtUtc
            ?? (result.CompletedAtUnixMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(result.CompletedAtUnixMs)
                : (DateTimeOffset?)null);
        if (completedAtUtc is not null && IsBeyondRetention(completedAtUtc.Value, _clock.UtcNow))
        {
            TryRemove(operationId, state);
            return null;
        }

        return result.Clone();
    }

    private async Task<OperationResult> ExecuteOnceAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        var startedAt = _clock.UtcNow;
        if (!ValidRequestShape(request))
        {
            return Result(
                request,
                OperationExecutionStatus.Failed,
                NetworkOperationErrorCode.ProtocolViolation,
                "OperationRequest is malformed.",
                startedAt);
        }

        if (_licenseStateProvider is not null
            && _licensePolicy.RequiresActiveCommercialLicense(request.OperationType)
            && !_licenseStateProvider.CurrentState.Active)
        {
            return Result(
                request,
                OperationExecutionStatus.Failed,
                NetworkOperationErrorCode.OperationRejected,
                "Commercial license is not active.",
                startedAt);
        }

        if (!IsKnownOperationType(request.OperationType)
            || !_handlers.TryGetValue(request.OperationType, out var handler))
        {
            return Result(
                request,
                OperationExecutionStatus.Failed,
                NetworkOperationErrorCode.OperationNotImplemented,
                "Operation is not implemented by this Agent.",
                startedAt);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeoutFor(request));

        try
        {
            RemoteOperationHandlerResult handlerResult =
                await handler.HandleAsync(request, timeout.Token).ConfigureAwait(false);
            return Result(
                request,
                handlerResult.Status,
                handlerResult.ErrorCode,
                handlerResult.Message,
                startedAt,
                handlerResult.WindowsSessionState);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            RemoteOperationHandlerResult timedOut = RemoteOperationHandlerResult.TimedOut();
            return Result(request, timedOut.Status, timedOut.ErrorCode, timedOut.Message, startedAt);
        }
    }

    private OperationAccepted Accepted(OperationRequest request)
    {
        return new OperationAccepted
        {
            OperationId = request.OperationId ?? string.Empty,
            OperationType = request.OperationType,
            TargetDeviceId = request.TargetDeviceId ?? string.Empty,
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            Status = OperationAcceptanceStatus.Accepted,
            AcceptedAtUnixMs = _clock.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private OperationResult DuplicateConflict(OperationRequest request)
    {
        var startedAt = _clock.UtcNow;
        return Result(
            request,
            OperationExecutionStatus.Failed,
            NetworkOperationErrorCode.OperationDuplicate,
            "OperationId was already used for a different request.",
            startedAt);
    }

    private OperationResult Result(
        OperationRequest request,
        OperationExecutionStatus status,
        NetworkOperationErrorCode errorCode,
        string message,
        DateTimeOffset startedAt,
        WindowsSessionStateResult? windowsSessionState = null)
    {
        var result = new OperationResult
        {
            OperationId = request.OperationId ?? string.Empty,
            OperationType = request.OperationType,
            TargetDeviceId = request.TargetDeviceId ?? string.Empty,
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            Status = status,
            ErrorCode = errorCode,
            Message = message ?? string.Empty,
            StartedAtUnixMs = startedAt.ToUnixTimeMilliseconds(),
            CompletedAtUnixMs = _clock.UtcNow.ToUnixTimeMilliseconds()
        };

        if (windowsSessionState is not null)
        {
            result.WindowsSessionState = windowsSessionState;
        }

        return result;
    }

    private TimeSpan TimeoutFor(OperationRequest request)
    {
        return request.TimeoutMs > 0
            ? TimeSpan.FromMilliseconds(Math.Min(request.TimeoutMs, (long)_options.Timeout.TotalMilliseconds))
            : _options.Timeout;
    }

    private static bool ValidRequestShape(OperationRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.OperationId)
            && !string.IsNullOrWhiteSpace(request.TargetDeviceId)
            && string.Equals(
                request.ProtocolVersion,
                MasterConnectionConstants.ProtocolVersion,
                StringComparison.Ordinal);
    }

    private static bool IsKnownOperationType(NetworkOperationType operationType)
    {
        return operationType != NetworkOperationType.Unspecified
            && Enum.IsDefined(typeof(NetworkOperationType), operationType);
    }

    private static bool SameRequest(RequestSignature left, RequestSignature right)
    {
        return left == right;
    }

    private sealed record RequestSignature(
        string OperationId,
        NetworkOperationType OperationType,
        string TargetDeviceId,
        string ProtocolVersion,
        OperationRequest.OperationParametersOneofCase OperationParametersCase,
        string ParameterSignature)
    {
        public static RequestSignature From(OperationRequest request)
        {
            return new RequestSignature(
                request.OperationId ?? string.Empty,
                request.OperationType,
                request.TargetDeviceId ?? string.Empty,
                request.ProtocolVersion ?? string.Empty,
                request.OperationParametersCase,
                ParameterSignatureFor(request));
        }

        private static string ParameterSignatureFor(OperationRequest request)
        {
            return request.OperationParametersCase switch
            {
                OperationRequest.OperationParametersOneofCase.OpenUrl => request.OpenUrl?.Url ?? string.Empty,
                OperationRequest.OperationParametersOneofCase.OpenApplication => request.OpenApplication?.ApplicationId ?? string.Empty,
                OperationRequest.OperationParametersOneofCase.ApplyBrowserPolicy => BrowserPolicySignature(
                    request.ApplyBrowserPolicy),
                OperationRequest.OperationParametersOneofCase.ApplyBrowserDownloadPolicy => BrowserDownloadPolicySignature(
                    request.ApplyBrowserDownloadPolicy),
                OperationRequest.OperationParametersOneofCase.ProvisionManagedCredential => ManagedCredentialProvisioningSignature(
                    request.ProvisionManagedCredential),
                OperationRequest.OperationParametersOneofCase.None => string.Empty,
                _ => "<unknown>"
            };
        }

        private static string BrowserPolicySignature(ApplyBrowserPolicyOperationParameters? parameters)
        {
            if (parameters is null)
            {
                return string.Empty;
            }

            var rules = string.Join(
                "\n",
                parameters.Rules.Select(rule =>
                    $"{rule.RuleId}\u001f{rule.Action}\u001f{rule.MatchType}\u001f{rule.Pattern}\u001f{rule.Enabled}"));
            return $"{parameters.PolicyId}\u001f{parameters.PolicyVersion}\u001f{parameters.ImplicitUnrestricted}\u001f{parameters.Mode}\u001f{parameters.AccountScope}\u001e{rules}";
        }

        private static string BrowserDownloadPolicySignature(
            ApplyBrowserDownloadPolicyOperationParameters? parameters)
        {
            return parameters is null
                ? string.Empty
                : $"{parameters.PolicyId}\u001f{parameters.PolicyVersion}\u001f{parameters.ImplicitNoSpecialRestrictions}\u001f{parameters.RestrictionMode}\u001f{parameters.AccountScope}";
        }

        private static string ManagedCredentialProvisioningSignature(
            ProvisionManagedCredentialOperationParameters? parameters)
        {
            return parameters is null
                ? string.Empty
                : parameters.AccountId.ToString();
        }
    }

    private static bool SameParameters(OperationRequest left, OperationRequest right)
    {
        if (left.OperationParametersCase != right.OperationParametersCase)
        {
            return false;
        }

        return left.OperationParametersCase switch
        {
            OperationRequest.OperationParametersOneofCase.OpenUrl => string.Equals(
                left.OpenUrl?.Url,
                right.OpenUrl?.Url,
                StringComparison.Ordinal),
            OperationRequest.OperationParametersOneofCase.OpenApplication => string.Equals(
                left.OpenApplication?.ApplicationId,
                right.OpenApplication?.ApplicationId,
                StringComparison.Ordinal),
            OperationRequest.OperationParametersOneofCase.ApplyBrowserPolicy => SameBrowserPolicyParameters(
                left.ApplyBrowserPolicy,
                right.ApplyBrowserPolicy),
            OperationRequest.OperationParametersOneofCase.ApplyBrowserDownloadPolicy => SameBrowserDownloadPolicyParameters(
                left.ApplyBrowserDownloadPolicy,
                right.ApplyBrowserDownloadPolicy),
            OperationRequest.OperationParametersOneofCase.None => true,
            _ => false
        };
    }

    private static bool SameBrowserPolicyParameters(
        ApplyBrowserPolicyOperationParameters? left,
        ApplyBrowserPolicyOperationParameters? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (!string.Equals(left.PolicyId, right.PolicyId, StringComparison.Ordinal)
            || left.PolicyVersion != right.PolicyVersion
            || left.ImplicitUnrestricted != right.ImplicitUnrestricted
            || left.Mode != right.Mode
            || left.AccountScope != right.AccountScope
            || left.Rules.Count != right.Rules.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Rules.Count; i++)
        {
            BrowserPolicyRuleParameters leftRule = left.Rules[i];
            BrowserPolicyRuleParameters rightRule = right.Rules[i];
            if (!string.Equals(leftRule.RuleId, rightRule.RuleId, StringComparison.Ordinal)
                || leftRule.Action != rightRule.Action
                || leftRule.MatchType != rightRule.MatchType
                || !string.Equals(leftRule.Pattern, rightRule.Pattern, StringComparison.Ordinal)
                || leftRule.Enabled != rightRule.Enabled)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameBrowserDownloadPolicyParameters(
        ApplyBrowserDownloadPolicyOperationParameters? left,
        ApplyBrowserDownloadPolicyOperationParameters? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(left.PolicyId, right.PolicyId, StringComparison.Ordinal)
            && left.PolicyVersion == right.PolicyVersion
            && left.ImplicitNoSpecialRestrictions == right.ImplicitNoSpecialRestrictions
            && left.RestrictionMode == right.RestrictionMode
            && left.AccountScope == right.AccountScope;
    }

    private void MaybeCleanupCompletedOperations()
    {
        var scanInterval = _options.DedupeCleanupScanInterval <= 0
            ? 64
            : _options.DedupeCleanupScanInterval;
        if (Interlocked.Increment(ref _dispatchCount) % scanInterval != 0)
        {
            return;
        }

        CleanupCompletedOperations(_clock.UtcNow);
    }

    private void CleanupCompletedOperations(DateTimeOffset nowUtc)
    {
        var retention = _options.DedupeRetention;
        var cutoffUtc = retention <= TimeSpan.Zero
            ? nowUtc.ToUniversalTime()
            : nowUtc.ToUniversalTime().Subtract(retention);

        foreach (var operation in _operations)
        {
            var completedAtUtc = operation.Value.CompletedAtUtc;
            if (completedAtUtc is not null && completedAtUtc.Value <= cutoffUtc)
            {
                TryRemove(operation.Key, operation.Value);
            }
        }

        var maxTracked = _options.MaxTrackedOperationIds;
        if (maxTracked <= 0 || _operations.Count <= maxTracked)
        {
            return;
        }

        var completedOperations = new List<KeyValuePair<string, OperationState>>();
        foreach (var operation in _operations)
        {
            if (operation.Value.CompletedAtUtc is not null)
            {
                completedOperations.Add(operation);
            }
        }

        completedOperations.Sort(static (left, right) =>
            Nullable.Compare(left.Value.CompletedAtUtc, right.Value.CompletedAtUtc));

        var overflow = _operations.Count - maxTracked;
        foreach (var operation in completedOperations)
        {
            if (overflow <= 0)
            {
                break;
            }

            if (TryRemove(operation.Key, operation.Value))
            {
                overflow--;
            }
        }
    }

    private bool TryRemove(string operationId, OperationState state)
    {
        return ((ICollection<KeyValuePair<string, OperationState>>)_operations).Remove(
            new KeyValuePair<string, OperationState>(operationId, state));
    }

    private bool IsBeyondRetention(DateTimeOffset completedAtUtc, DateTimeOffset nowUtc)
    {
        var retention = _options.DedupeRetention;
        var cutoffUtc = retention <= TimeSpan.Zero
            ? nowUtc.ToUniversalTime()
            : nowUtc.ToUniversalTime().Subtract(retention);
        return completedAtUtc <= cutoffUtc;
    }

    private sealed class OperationState
    {
        private long _completedAtUnixMs;

        public OperationState(
            RequestSignature signature,
            Lazy<Task<OperationResult>> result)
        {
            Signature = signature;
            Result = result;
        }

        public RequestSignature Signature { get; }

        public Lazy<Task<OperationResult>> Result { get; }

        public DateTimeOffset? CompletedAtUtc
        {
            get
            {
                var completedAtUnixMs = Interlocked.Read(ref _completedAtUnixMs);
                return completedAtUnixMs == 0
                    ? null
                    : DateTimeOffset.FromUnixTimeMilliseconds(completedAtUnixMs);
            }
        }

        public void MarkCompleted(long completedAtUnixMs)
        {
            if (completedAtUnixMs > 0)
            {
                Interlocked.CompareExchange(ref _completedAtUnixMs, completedAtUnixMs, 0);
            }
        }
    }
}

public sealed record RemoteOperationDispatchResult(
    OperationAccepted Accepted,
    OperationResult Result,
    bool Duplicate);
