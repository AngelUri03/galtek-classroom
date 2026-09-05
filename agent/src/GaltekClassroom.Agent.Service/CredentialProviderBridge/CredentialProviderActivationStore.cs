using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.CredentialProviderBridge;

public enum CredentialProviderActivationState
{
    Pending,
    IdentityResolved,
    Consumed,
    Completed
}

public enum CredentialProviderActivationSource
{
    Local,
    Remote
}

public enum CredentialProviderLogonCompletionOutcome
{
    Success,
    Failed,
    LocalSerializationFailed,
    TimedOut
}

public sealed record CredentialProviderActivation(
    string ActivationId,
    string AccountId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? OperationId = null,
    bool AutoSubmitRequested = false,
    CredentialProviderActivationSource Source = CredentialProviderActivationSource.Local,
    CredentialProviderActivationState State = CredentialProviderActivationState.Pending,
    string? ExpectedWindowsSid = null)
{
    public CredentialProviderActivationMetadata ToMetadata()
    {
        return new CredentialProviderActivationMetadata
        {
            ActivationId = ActivationId,
            AccountId = AccountId,
            CreatedAtUtc = CreatedAtUtc,
            ExpiresAtUtc = ExpiresAtUtc
        };
    }
}

public enum CredentialProviderActivationSetStatus
{
    Activated,
    Busy,
    Rejected
}

public sealed record CredentialProviderActivationSetResult(
    CredentialProviderActivationSetStatus Status,
    CredentialProviderActivation? Activation,
    string? ErrorMessage)
{
    public bool Succeeded => Status == CredentialProviderActivationSetStatus.Activated;

    public static CredentialProviderActivationSetResult Activated(CredentialProviderActivation activation)
    {
        return new CredentialProviderActivationSetResult(
            CredentialProviderActivationSetStatus.Activated,
            activation,
            null);
    }

    public static CredentialProviderActivationSetResult Rejected(string errorMessage)
    {
        return new CredentialProviderActivationSetResult(
            CredentialProviderActivationSetStatus.Rejected,
            null,
            errorMessage);
    }

    public static CredentialProviderActivationSetResult Busy(string errorMessage)
    {
        return new CredentialProviderActivationSetResult(
            CredentialProviderActivationSetStatus.Busy,
            null,
            errorMessage);
    }
}

public interface ICredentialProviderActivationStore
{
    CredentialProviderActivationSetResult SetPending(
        string accountId,
        DateTimeOffset nowUtc,
        TimeSpan ttl);

    CredentialProviderActivationSetResult SetRemotePending(
        string operationId,
        string accountId,
        DateTimeOffset nowUtc,
        TimeSpan ttl,
        bool autoSubmitRequested);

    CredentialProviderActivation? GetPending(DateTimeOffset nowUtc);

    long CurrentGeneration(DateTimeOffset nowUtc);

    int ListenerCount { get; }

    Task<long> WaitForGenerationChangeAsync(
        long observedGeneration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<bool> WaitForListenerAsync(TimeSpan timeout, CancellationToken cancellationToken);

    bool TrySnapshotIdentity(
        string activationId,
        string windowsSid,
        DateTimeOffset nowUtc,
        out CredentialProviderActivation? activation);

    bool TryConsume(
        string activationId,
        string expectedWindowsSid,
        DateTimeOffset nowUtc,
        out CredentialProviderActivation? activation);

    bool TryComplete(
        string activationId,
        CredentialProviderLogonCompletionOutcome outcome,
        DateTimeOffset nowUtc);

    Task<CredentialProviderLogonCompletionOutcome> WaitForCompletionAsync(
        string operationId,
        string activationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    bool TryTimeout(
        string operationId,
        string activationId,
        DateTimeOffset nowUtc);

    void Clear();
}

public sealed class CredentialProviderActivationStore : ICredentialProviderActivationStore
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan MaximumTtl = TimeSpan.FromSeconds(60);

    private readonly object _syncRoot = new();
    private CredentialProviderActivation? _pending;
    private long _generation;
    private int _listenerCount;
    private TaskCompletionSource<long> _generationChanged =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool> _listenerChanged =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<CredentialProviderLogonCompletionOutcome>? _completion;
    private string? _completionOperationId;
    private string? _completionActivationId;

    public CredentialProviderActivationSetResult SetPending(
        string accountId,
        DateTimeOffset nowUtc,
        TimeSpan ttl)
    {
        if (!ManagedWindowsAccountBinding.IsValidAccountId(accountId))
        {
            return CredentialProviderActivationSetResult.Rejected(
                "Credential Provider activation accountId must be PRIMARY or SECONDARY.");
        }

        if (ttl <= TimeSpan.Zero || ttl > MaximumTtl)
        {
            return CredentialProviderActivationSetResult.Rejected(
                "Credential Provider activation TTL must be positive and no longer than 60 seconds.");
        }

        var createdAtUtc = nowUtc.ToUniversalTime();
        var activation = new CredentialProviderActivation(
            Guid.NewGuid().ToString("D"),
            ManagedWindowsAccountBinding.NormalizeAccountId(accountId),
            createdAtUtc,
            createdAtUtc.Add(ttl));

        lock (_syncRoot)
        {
            _pending = activation;
            _completion = null;
            _completionOperationId = null;
            _completionActivationId = null;
            IncrementGenerationLocked();
        }

        return CredentialProviderActivationSetResult.Activated(activation);
    }

    public CredentialProviderActivationSetResult SetRemotePending(
        string operationId,
        string accountId,
        DateTimeOffset nowUtc,
        TimeSpan ttl,
        bool autoSubmitRequested)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return CredentialProviderActivationSetResult.Rejected(
                "Credential Provider activation operationId is required.");
        }

        if (!ManagedWindowsAccountBinding.IsValidAccountId(accountId))
        {
            return CredentialProviderActivationSetResult.Rejected(
                "Credential Provider activation accountId must be PRIMARY or SECONDARY.");
        }

        if (ttl <= TimeSpan.Zero || ttl > MaximumTtl)
        {
            return CredentialProviderActivationSetResult.Rejected(
                "Credential Provider activation TTL must be positive and no longer than 60 seconds.");
        }

        var createdAtUtc = nowUtc.ToUniversalTime();
        var activation = new CredentialProviderActivation(
            Guid.NewGuid().ToString("D"),
            ManagedWindowsAccountBinding.NormalizeAccountId(accountId),
            createdAtUtc,
            createdAtUtc.Add(ttl),
            operationId,
            autoSubmitRequested,
            CredentialProviderActivationSource.Remote);

        lock (_syncRoot)
        {
            ExpireIfNeeded(nowUtc);
            if (_pending is not null
                && _pending.Source == CredentialProviderActivationSource.Remote
                && _pending.State != CredentialProviderActivationState.Completed
                && !string.Equals(_pending.OperationId, operationId, StringComparison.Ordinal))
            {
                return CredentialProviderActivationSetResult.Busy(
                    "Another managed Windows logon activation is already pending.");
            }

            _pending = activation;
            _completion = new TaskCompletionSource<CredentialProviderLogonCompletionOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _completionOperationId = operationId;
            _completionActivationId = activation.ActivationId;
            IncrementGenerationLocked();
        }

        return CredentialProviderActivationSetResult.Activated(activation);
    }

    public CredentialProviderActivation? GetPending(DateTimeOffset nowUtc)
    {
        lock (_syncRoot)
        {
            if (_pending is null)
            {
                return null;
            }

            if (ExpireIfNeeded(nowUtc))
            {
                return null;
            }

            return _pending.State == CredentialProviderActivationState.Consumed ? null : _pending;
        }
    }

    public long CurrentGeneration(DateTimeOffset nowUtc)
    {
        lock (_syncRoot)
        {
            ExpireIfNeeded(nowUtc);
            return _generation;
        }
    }

    public int ListenerCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _listenerCount;
            }
        }
    }

    public async Task<long> WaitForGenerationChangeAsync(
        long observedGeneration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        Task<long> waitTask;
        lock (_syncRoot)
        {
            ExpireIfNeeded(nowUtc);
            if (_generation != observedGeneration)
            {
                return _generation;
            }

            _listenerCount++;
            _listenerChanged.TrySetResult(true);
            _listenerChanged = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            waitTask = _generationChanged.Task;
        }

        try
        {
            return await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_syncRoot)
            {
                _listenerCount = Math.Max(0, _listenerCount - 1);
                _listenerChanged.TrySetResult(true);
                _listenerChanged = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    public async Task<bool> WaitForListenerAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            lock (_syncRoot)
            {
                return _listenerCount > 0;
            }
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        while (true)
        {
            Task waitTask;
            lock (_syncRoot)
            {
                if (_listenerCount > 0)
                {
                    return true;
                }

                waitTask = _listenerChanged.Task;
            }

            try
            {
                await waitTask.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }
    }

    public bool TrySnapshotIdentity(
        string activationId,
        string windowsSid,
        DateTimeOffset nowUtc,
        out CredentialProviderActivation? activation)
    {
        activation = null;
        if (string.IsNullOrWhiteSpace(activationId) || string.IsNullOrWhiteSpace(windowsSid))
        {
            return false;
        }

        lock (_syncRoot)
        {
            if (ExpireIfNeeded(nowUtc)
                || _pending is null
                || _pending.State == CredentialProviderActivationState.Consumed
                || !string.Equals(_pending.ActivationId, activationId, StringComparison.Ordinal))
            {
                return false;
            }

            if (_pending.ExpectedWindowsSid is not null
                && !string.Equals(_pending.ExpectedWindowsSid, windowsSid, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _pending = _pending with
            {
                State = CredentialProviderActivationState.IdentityResolved,
                ExpectedWindowsSid = windowsSid
            };
            activation = _pending;
            return true;
        }
    }

    public bool TryConsume(
        string activationId,
        string expectedWindowsSid,
        DateTimeOffset nowUtc,
        out CredentialProviderActivation? activation)
    {
        activation = null;
        if (string.IsNullOrWhiteSpace(activationId) || string.IsNullOrWhiteSpace(expectedWindowsSid))
        {
            return false;
        }

        lock (_syncRoot)
        {
            if (ExpireIfNeeded(nowUtc)
                || _pending is null
                || _pending.State != CredentialProviderActivationState.IdentityResolved
                || !string.Equals(_pending.ActivationId, activationId, StringComparison.Ordinal)
                || !string.Equals(_pending.ExpectedWindowsSid, expectedWindowsSid, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _pending = _pending with { State = CredentialProviderActivationState.Consumed };
            activation = _pending;
            IncrementGenerationLocked();
            return true;
        }
    }

    public bool TryComplete(
        string activationId,
        CredentialProviderLogonCompletionOutcome outcome,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(activationId)
            || outcome == CredentialProviderLogonCompletionOutcome.TimedOut)
        {
            return false;
        }

        lock (_syncRoot)
        {
            if (ExpireIfNeeded(nowUtc)
                || _pending is null
                || _pending.Source != CredentialProviderActivationSource.Remote
                || _pending.State is CredentialProviderActivationState.Completed
                || !string.Equals(_pending.ActivationId, activationId, StringComparison.Ordinal))
            {
                return false;
            }

            _pending = _pending with { State = CredentialProviderActivationState.Completed };
            _completion?.TrySetResult(outcome);
            _pending = null;
            IncrementGenerationLocked();
        }

        return true;
    }

    public async Task<CredentialProviderLogonCompletionOutcome> WaitForCompletionAsync(
        string operationId,
        string activationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        Task<CredentialProviderLogonCompletionOutcome>? completionTask = null;
        Task<CredentialProviderLogonCompletionOutcome>? alreadyCompletedTask = null;
        TimeSpan remaining;
        lock (_syncRoot)
        {
            if (_completion is not null
                && string.Equals(_completionOperationId, operationId, StringComparison.Ordinal)
                && string.Equals(_completionActivationId, activationId, StringComparison.Ordinal)
                && _completion.Task.IsCompleted)
            {
                alreadyCompletedTask = _completion.Task;
                remaining = TimeSpan.Zero;
            }
            else
            {
                if (ExpireIfNeeded(nowUtc)
                    || _pending is null
                    || _completion is null
                    || !string.Equals(_pending.OperationId, operationId, StringComparison.Ordinal)
                    || !string.Equals(_pending.ActivationId, activationId, StringComparison.Ordinal))
                {
                    return CredentialProviderLogonCompletionOutcome.TimedOut;
                }

                completionTask = _completion.Task;
                remaining = _pending.ExpiresAtUtc - nowUtc.ToUniversalTime();
            }
        }

        if (alreadyCompletedTask is not null)
        {
            return await alreadyCompletedTask.ConfigureAwait(false);
        }

        if (remaining <= TimeSpan.Zero)
        {
            TryTimeout(operationId, activationId, nowUtc);
            return CredentialProviderLogonCompletionOutcome.TimedOut;
        }

        using var timeout = new CancellationTokenSource(remaining);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await completionTask!.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryTimeout(operationId, activationId, DateTimeOffset.UtcNow);
            return CredentialProviderLogonCompletionOutcome.TimedOut;
        }
    }

    public bool TryTimeout(
        string operationId,
        string activationId,
        DateTimeOffset nowUtc)
    {
        TaskCompletionSource<CredentialProviderLogonCompletionOutcome>? completion;
        lock (_syncRoot)
        {
            if (_pending is null
                || _pending.Source != CredentialProviderActivationSource.Remote
                || !string.Equals(_pending.OperationId, operationId, StringComparison.Ordinal)
                || !string.Equals(_pending.ActivationId, activationId, StringComparison.Ordinal))
            {
                return false;
            }

            _pending = null;
            completion = _completion;
            _completion = null;
            _completionOperationId = null;
            _completionActivationId = null;
            IncrementGenerationLocked();
        }

        completion?.TrySetResult(CredentialProviderLogonCompletionOutcome.TimedOut);
        return true;
    }

    public void Clear()
    {
        TaskCompletionSource<CredentialProviderLogonCompletionOutcome>? completion;
        lock (_syncRoot)
        {
            completion = _completion;
            _pending = null;
            _completion = null;
            _completionOperationId = null;
            _completionActivationId = null;
            IncrementGenerationLocked();
        }

        completion?.TrySetResult(CredentialProviderLogonCompletionOutcome.TimedOut);
    }

    private bool ExpireIfNeeded(DateTimeOffset nowUtc)
    {
        if (_pending is null)
        {
            return false;
        }

        if (nowUtc.ToUniversalTime() <= _pending.ExpiresAtUtc)
        {
            return false;
        }

        var completion = _completion;
        _pending = null;
        _completion = null;
        _completionOperationId = null;
        _completionActivationId = null;
        IncrementGenerationLocked();
        completion?.TrySetResult(CredentialProviderLogonCompletionOutcome.TimedOut);
        return true;
    }

    private void IncrementGenerationLocked()
    {
        var next = unchecked(++_generation);
        _generationChanged.TrySetResult(next);
        _generationChanged = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed class CredentialProviderActivationService
{
    private readonly ICredentialProviderActivationStore _activationStore;
    private readonly ISystemClock _clock;
    private readonly InstallationIdentityStore? _installationIdentityStore;
    private readonly IManagedWindowsAccountBindingStore? _bindingStore;
    private readonly IWindowsAccountResolver? _accountResolver;
    private readonly IManagedWindowsCredentialStore? _credentialStore;

    public CredentialProviderActivationService(
        ICredentialProviderActivationStore activationStore,
        ISystemClock clock)
        : this(
            activationStore,
            clock,
            null,
            null,
            null,
            null)
    {
    }

    public CredentialProviderActivationService(
        ICredentialProviderActivationStore activationStore,
        ISystemClock clock,
        InstallationIdentityStore? installationIdentityStore,
        IManagedWindowsAccountBindingStore? bindingStore,
        IWindowsAccountResolver? accountResolver,
        IManagedWindowsCredentialStore? credentialStore)
    {
        _activationStore = activationStore;
        _clock = clock;
        _installationIdentityStore = installationIdentityStore;
        _bindingStore = bindingStore;
        _accountResolver = accountResolver;
        _credentialStore = credentialStore;
    }

    public CredentialProviderActivationMetadata? GetPendingMetadata()
    {
        return _activationStore.GetPending(_clock.UtcNow)?.ToMetadata();
    }

    public async Task<CredentialProviderActivationIdentity?> GetPendingIdentityAsync(
        CancellationToken cancellationToken)
    {
        var pending = _activationStore.GetPending(_clock.UtcNow);
        if (pending is null || !HasIdentityDependencies())
        {
            return null;
        }

        var installation = await _installationIdentityStore!.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (installation.Status != InstallationIdentityStoreReadStatus.Loaded || installation.Identity is null)
        {
            return null;
        }

        var binding = await LoadBindingAsync(
            installation.Identity.InstallationId,
            pending.AccountId,
            cancellationToken).ConfigureAwait(false);
        if (binding is null)
        {
            return null;
        }

        var resolved = ResolveBindingSid(binding);
        if (resolved is null)
        {
            return null;
        }

        if (!_activationStore.TrySnapshotIdentity(
                pending.ActivationId,
                resolved.WindowsSid,
                _clock.UtcNow,
                out var snapshotted)
            || snapshotted is null)
        {
            return null;
        }

        return new CredentialProviderActivationIdentity
        {
            ActivationId = snapshotted.ActivationId,
            AccountId = snapshotted.AccountId,
            UserSid = resolved.WindowsSid,
            Domain = resolved.Domain!,
            Username = resolved.Username!,
            AutoSubmitRequested = snapshotted.AutoSubmitRequested
        };
    }

    public async Task<byte[]?> AcquirePendingCredentialAsync(
        string? activationId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(activationId, out _) || !HasAcquireDependencies())
        {
            return null;
        }

        var pending = _activationStore.GetPending(_clock.UtcNow);
        if (pending is null
            || pending.State != CredentialProviderActivationState.IdentityResolved
            || !string.Equals(pending.ActivationId, activationId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(pending.ExpectedWindowsSid))
        {
            return null;
        }

        var installation = await _installationIdentityStore!.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (installation.Status != InstallationIdentityStoreReadStatus.Loaded || installation.Identity is null)
        {
            return null;
        }

        var binding = await LoadBindingAsync(
            installation.Identity.InstallationId,
            pending.AccountId,
            cancellationToken).ConfigureAwait(false);
        if (binding is null
            || !string.Equals(binding.WindowsSid, pending.ExpectedWindowsSid, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!_activationStore.TryConsume(
                pending.ActivationId,
                pending.ExpectedWindowsSid,
                _clock.UtcNow,
                out var consumed)
            || consumed is null)
        {
            return null;
        }

        var acquired = await _credentialStore!.AcquireForWindowsSidAsync(
            installation.Identity.InstallationId,
            consumed.AccountId,
            consumed.ExpectedWindowsSid!,
            cancellationToken).ConfigureAwait(false);
        if (!acquired.Succeeded || acquired.Lease is null)
        {
            return null;
        }

        using var lease = acquired.Lease;
        if (!string.Equals(lease.WindowsSid, consumed.ExpectedWindowsSid, StringComparison.OrdinalIgnoreCase)
            || lease.PasswordUtf16LittleEndian.Length == 0)
        {
            return null;
        }

        return CredentialProviderSecretResponse.Success(
            consumed.ActivationId,
            lease.PasswordUtf16LittleEndian.Span);
    }

    public long CurrentGeneration()
    {
        return _activationStore.CurrentGeneration(_clock.UtcNow);
    }

    public Task<long> WaitForGenerationChangeAsync(
        long observedGeneration,
        CancellationToken cancellationToken)
    {
        return _activationStore.WaitForGenerationChangeAsync(
            observedGeneration,
            _clock.UtcNow,
            cancellationToken);
    }

    public Task<bool> WaitForListenerAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return _activationStore.WaitForListenerAsync(timeout, cancellationToken);
    }

    public bool TryCompleteLogon(
        string? activationId,
        string? outcome)
    {
        if (!Guid.TryParse(activationId, out _))
        {
            return false;
        }

        CredentialProviderLogonCompletionOutcome completionOutcome = outcome switch
        {
            CredentialProviderLogonResultOutcomes.Success => CredentialProviderLogonCompletionOutcome.Success,
            CredentialProviderLogonResultOutcomes.Failed => CredentialProviderLogonCompletionOutcome.Failed,
            CredentialProviderLogonResultOutcomes.LocalSerializationFailed =>
                CredentialProviderLogonCompletionOutcome.LocalSerializationFailed,
            _ => throw new ArgumentException("Credential Provider logon outcome is invalid.", nameof(outcome))
        };

        return _activationStore.TryComplete(activationId, completionOutcome, _clock.UtcNow);
    }

    public CredentialProviderActivationSetResult SetRemotePending(
        string operationId,
        string accountId,
        TimeSpan ttl,
        bool autoSubmitRequested)
    {
        return _activationStore.SetRemotePending(
            operationId,
            accountId,
            _clock.UtcNow,
            ttl,
            autoSubmitRequested);
    }

    public Task<CredentialProviderLogonCompletionOutcome> WaitForCompletionAsync(
        string operationId,
        string activationId,
        CancellationToken cancellationToken)
    {
        return _activationStore.WaitForCompletionAsync(
            operationId,
            activationId,
            _clock.UtcNow,
            cancellationToken);
    }

    private async Task<ManagedWindowsAccountBinding?> LoadBindingAsync(
        Guid installationId,
        string accountId,
        CancellationToken cancellationToken)
    {
        var bindings = await _bindingStore!.GetAsync(
            installationId,
            accountId,
            cancellationToken).ConfigureAwait(false);
        if (!bindings.Loaded || bindings.Bindings.Count != 1)
        {
            return null;
        }

        return bindings.Bindings[0];
    }

    private WindowsAccountIdentity? ResolveBindingSid(ManagedWindowsAccountBinding binding)
    {
        if (!MasterBindingValidator.IsValidSid(binding.WindowsSid))
        {
            return null;
        }

        var resolved = _accountResolver!.ResolveSid(binding.WindowsSid);
        if (!resolved.Found
            || resolved.Identity is null
            || resolved.Identity.SidNameUse != WindowsAccountSidNameUse.User
            || string.IsNullOrWhiteSpace(resolved.Identity.Domain)
            || string.IsNullOrWhiteSpace(resolved.Identity.Username)
            || !string.Equals(resolved.Identity.WindowsSid, binding.WindowsSid, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return resolved.Identity;
    }

    private bool HasIdentityDependencies()
    {
        return _installationIdentityStore is not null
            && _bindingStore is not null
            && _accountResolver is not null;
    }

    private bool HasAcquireDependencies()
    {
        return HasIdentityDependencies() && _credentialStore is not null;
    }
}
