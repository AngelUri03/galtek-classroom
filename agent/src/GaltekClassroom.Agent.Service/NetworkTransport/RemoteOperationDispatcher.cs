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
    private readonly ConcurrentDictionary<string, OperationState> _operations = new(StringComparer.Ordinal);

    public RemoteOperationDispatcher(
        IEnumerable<IRemoteOperationHandler> handlers,
        RemoteOperationOptions options,
        ISystemClock clock,
        ILicenseStateProvider? licenseStateProvider = null)
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
    }

    public async Task<RemoteOperationDispatchResult> DispatchAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = new OperationState(
            request,
            new Lazy<Task<OperationResult>>(
                () => ExecuteOnceAsync(request, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var existing = _operations.GetOrAdd(request.OperationId ?? string.Empty, state);
        var duplicate = !ReferenceEquals(existing, state);

        OperationResult result;
        if (duplicate && !SameRequest(existing.Request, request))
        {
            result = DuplicateConflict(request);
        }
        else
        {
            result = await existing.Result.Value.ConfigureAwait(false);
        }

        return new RemoteOperationDispatchResult(
            Accepted(request),
            result,
            duplicate);
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

        if (_licenseStateProvider is not null && !_licenseStateProvider.CurrentState.Active)
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
                startedAt);
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
        DateTimeOffset startedAt)
    {
        return new OperationResult
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

    private static bool SameRequest(OperationRequest left, OperationRequest right)
    {
        return string.Equals(left.OperationId, right.OperationId, StringComparison.Ordinal)
            && left.OperationType == right.OperationType
            && string.Equals(left.TargetDeviceId, right.TargetDeviceId, StringComparison.Ordinal)
            && string.Equals(left.ProtocolVersion, right.ProtocolVersion, StringComparison.Ordinal);
    }

    private sealed record OperationState(
        OperationRequest Request,
        Lazy<Task<OperationResult>> Result);
}

public sealed record RemoteOperationDispatchResult(
    OperationAccepted Accepted,
    OperationResult Result,
    bool Duplicate);
