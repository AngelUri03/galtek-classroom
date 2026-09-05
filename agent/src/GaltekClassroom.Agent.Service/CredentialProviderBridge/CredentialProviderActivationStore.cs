using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.CredentialProviderBridge;

public enum CredentialProviderActivationState
{
    Pending,
    IdentityResolved,
    Consumed
}

public sealed record CredentialProviderActivation(
    string ActivationId,
    string AccountId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
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
}

public interface ICredentialProviderActivationStore
{
    CredentialProviderActivationSetResult SetPending(
        string accountId,
        DateTimeOffset nowUtc,
        TimeSpan ttl);

    CredentialProviderActivation? GetPending(DateTimeOffset nowUtc);

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

    void Clear();
}

public sealed class CredentialProviderActivationStore : ICredentialProviderActivationStore
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan MaximumTtl = TimeSpan.FromSeconds(60);

    private readonly object _syncRoot = new();
    private CredentialProviderActivation? _pending;

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
            return true;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _pending = null;
        }
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

        _pending = null;
        return true;
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
            Username = resolved.Username!
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
