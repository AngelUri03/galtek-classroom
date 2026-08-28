using GaltekClassroom.Agent.Service.Ipc;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.Runtime;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Master;

public sealed class MasterAuthorizationService
{
    private readonly AgentRuntimeState _runtimeState;
    private readonly ILicenseStateProvider _licenseStateProvider;
    private readonly MasterBindingStore _bindingStore;
    private readonly ILogger<MasterAuthorizationService> _logger;

    public MasterAuthorizationService(
        AgentRuntimeState runtimeState,
        ILicenseStateProvider licenseStateProvider,
        MasterBindingStore bindingStore,
        ILogger<MasterAuthorizationService> logger)
    {
        _runtimeState = runtimeState;
        _licenseStateProvider = licenseStateProvider;
        _bindingStore = bindingStore;
        _logger = logger;
    }

    public async Task<LocalMasterAuthorization> GetAuthorizationAsync(
        LocalIpcClientContext clientContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientContext);

        var bindingResult = await _bindingStore.ReadAsync(cancellationToken);
        var authorization = Evaluate(
            _runtimeState.GetInstallationIdentity(),
            _licenseStateProvider.CurrentState,
            bindingResult,
            clientContext);

        if (authorization.Authorized)
        {
            _logger.LogDebug("Master authorization evaluated: {Status}.", authorization.Status);
        }
        else
        {
            _logger.LogInformation("Master authorization evaluated: {Status}.", authorization.Status);
        }

        return authorization;
    }

    public static LocalMasterAuthorization Evaluate(
        InstallationIdentity installationIdentity,
        LicenseState licenseState,
        MasterBindingStoreReadResult bindingResult,
        LocalIpcClientContext clientContext)
    {
        ArgumentNullException.ThrowIfNull(installationIdentity);
        ArgumentNullException.ThrowIfNull(licenseState);
        ArgumentNullException.ThrowIfNull(bindingResult);
        ArgumentNullException.ThrowIfNull(clientContext);

        if (bindingResult.Status == MasterBindingStoreReadStatus.Missing)
        {
            return Unauthorized(
                MasterAuthorizationStatus.NotConfigured,
                configured: false,
                boundAccountDisplayName: null,
                currentAccountDisplayName: clientContext.AccountName);
        }

        if (bindingResult.Status == MasterBindingStoreReadStatus.Invalid)
        {
            return Unauthorized(
                MasterAuthorizationStatus.MasterBindingInvalid,
                configured: true,
                boundAccountDisplayName: null,
                currentAccountDisplayName: clientContext.AccountName);
        }

        var binding = bindingResult.Binding!;

        if (binding.InstallationId != installationIdentity.InstallationId)
        {
            return Unauthorized(
                MasterAuthorizationStatus.InstallationMismatch,
                configured: true,
                binding.AccountDisplayName,
                clientContext.AccountName);
        }

        if (!licenseState.Active || !ContainsMasterRole(licenseState.Roles))
        {
            return Unauthorized(
                MasterAuthorizationStatus.MasterLicenseRequired,
                configured: true,
                binding.AccountDisplayName,
                clientContext.AccountName);
        }

        if (!MasterBindingValidator.IsValidSid(clientContext.WindowsSid)
            || !string.Equals(binding.WindowsSid, clientContext.WindowsSid, StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized(
                MasterAuthorizationStatus.CurrentAccountNotAuthorized,
                configured: true,
                binding.AccountDisplayName,
                clientContext.AccountName);
        }

        return new LocalMasterAuthorization
        {
            Status = MasterAuthorizationStatus.Authorized.ToCode(),
            Authorized = true,
            Configured = true,
            BoundAccountDisplayName = binding.AccountDisplayName,
            CurrentAccountDisplayName = clientContext.AccountName
        };
    }

    private static LocalMasterAuthorization Unauthorized(
        MasterAuthorizationStatus status,
        bool configured,
        string? boundAccountDisplayName,
        string? currentAccountDisplayName)
    {
        return new LocalMasterAuthorization
        {
            Status = status.ToCode(),
            Authorized = false,
            Configured = configured,
            BoundAccountDisplayName = boundAccountDisplayName,
            CurrentAccountDisplayName = currentAccountDisplayName
        };
    }

    private static bool ContainsMasterRole(IReadOnlyList<string> roles)
    {
        return roles.Any(role =>
            string.Equals(role, CommercialLicenseConstants.MasterRole, StringComparison.Ordinal));
    }
}
