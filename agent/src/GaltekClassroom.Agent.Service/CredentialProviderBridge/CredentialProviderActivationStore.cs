using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.CredentialProviderBridge;

public sealed record CredentialProviderActivation(
    string ActivationId,
    string AccountId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc)
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

            if (nowUtc.ToUniversalTime() > _pending.ExpiresAtUtc)
            {
                _pending = null;
                return null;
            }

            return _pending;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _pending = null;
        }
    }
}

public sealed class CredentialProviderActivationService
{
    private readonly ICredentialProviderActivationStore _activationStore;
    private readonly ISystemClock _clock;

    public CredentialProviderActivationService(
        ICredentialProviderActivationStore activationStore,
        ISystemClock clock)
    {
        _activationStore = activationStore;
        _clock = clock;
    }

    public CredentialProviderActivationMetadata? GetPendingMetadata()
    {
        return _activationStore.GetPending(_clock.UtcNow)?.ToMetadata();
    }
}
