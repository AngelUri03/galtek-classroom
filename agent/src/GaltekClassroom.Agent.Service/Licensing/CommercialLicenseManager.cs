using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Licensing;

public sealed class CommercialLicenseManager
{
    private readonly CommercialLicenseStore _store;
    private readonly CommercialLicenseValidator _validator;
    private readonly ISystemClock _clock;
    private readonly object _sync = new();

    private LicenseState _currentState;

    public CommercialLicenseManager(
        CommercialLicenseStore store,
        CommercialLicenseValidator validator,
        ISystemClock clock)
    {
        _store = store;
        _validator = validator;
        _clock = clock;
        _currentState = LicenseState.Blocked(
            CommercialLicenseStatus.ActivationRequired,
            _clock.UtcNow,
            "Commercial license has not been resolved yet.");
    }

    public LicenseState CurrentState
    {
        get
        {
            lock (_sync)
            {
                return _currentState;
            }
        }
    }

    public async Task<LicenseState> ResolveAsync(
        InstallationIdentity installationIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installationIdentity);

        var readResult = await _store.ReadAsync(cancellationToken);

        if (readResult.Status == CommercialLicenseStoreReadStatus.Missing)
        {
            return SetCurrentState(
                LicenseState.Blocked(
                    CommercialLicenseStatus.ActivationRequired,
                    _clock.UtcNow,
                    $"Commercial license file was not found at {readResult.FilePath}."));
        }

        if (readResult.Status == CommercialLicenseStoreReadStatus.Invalid)
        {
            return SetCurrentState(
                LicenseState.Blocked(
                    CommercialLicenseStatus.LicenseMalformed,
                    _clock.UtcNow,
                    readResult.ErrorMessage ?? "Commercial license file is invalid."));
        }

        var validation = await _validator.ValidateAsync(
            readResult.Token!,
            installationIdentity,
            cancellationToken);

        return SetCurrentState(validation.State);
    }

    public async Task<CommercialLicenseActivationResult> ActivateAsync(
        string candidateToken,
        InstallationIdentity installationIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installationIdentity);

        var validation = await _validator.ValidateAsync(
            candidateToken,
            installationIdentity,
            cancellationToken);

        if (!validation.IsActive)
        {
            return new CommercialLicenseActivationResult(
                Activated: false,
                validation.State,
                CurrentState);
        }

        await _store.WriteAsync(candidateToken, cancellationToken);
        var currentState = SetCurrentState(validation.State);

        return new CommercialLicenseActivationResult(
            Activated: true,
            currentState,
            currentState);
    }

    public LicenseState RefreshExpirationOnly()
    {
        lock (_sync)
        {
            if (!_currentState.Active || _currentState.ExpiresAtUtc is null)
            {
                return _currentState;
            }

            var nowUtc = _clock.UtcNow.ToUniversalTime();

            if (nowUtc < _currentState.ExpiresAtUtc.Value)
            {
                return _currentState;
            }

            _currentState = LicenseState.Blocked(
                CommercialLicenseStatus.LicenseExpired,
                nowUtc,
                "License expired while the Service was running.",
                _currentState.ExpiresAtUtc,
                _currentState.LicenseId,
                _currentState.OrganizationId,
                _currentState.Roles,
                _currentState.Features);

            return _currentState;
        }
    }

    private LicenseState SetCurrentState(LicenseState state)
    {
        lock (_sync)
        {
            _currentState = state;
            return _currentState;
        }
    }
}
