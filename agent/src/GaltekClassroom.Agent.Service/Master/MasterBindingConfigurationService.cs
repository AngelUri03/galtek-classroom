using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Master;

public enum MasterBindingConfigurationStatus
{
    Configured,
    AdministratorRequired,
    WindowsAccountNotFound,
    MasterBindingAlreadyConfigured,
    MasterBindingInvalid,
    InstallationIdentityInvalid
}

public static class MasterBindingConfigurationStatusExtensions
{
    public static string ToCode(this MasterBindingConfigurationStatus status)
    {
        return status switch
        {
            MasterBindingConfigurationStatus.Configured => "MASTER_BINDING_CONFIGURED",
            MasterBindingConfigurationStatus.AdministratorRequired => "ADMINISTRATOR_REQUIRED",
            MasterBindingConfigurationStatus.WindowsAccountNotFound => "WINDOWS_ACCOUNT_NOT_FOUND",
            MasterBindingConfigurationStatus.MasterBindingAlreadyConfigured => "MASTER_BINDING_ALREADY_CONFIGURED",
            MasterBindingConfigurationStatus.MasterBindingInvalid => "MASTER_BINDING_INVALID",
            MasterBindingConfigurationStatus.InstallationIdentityInvalid => "INSTALLATION_IDENTITY_INVALID",
            _ => "MASTER_BINDING_INVALID"
        };
    }
}

public sealed record MasterBindingConfigurationResult(
    MasterBindingConfigurationStatus Status,
    bool Configured,
    string? AccountDisplayName,
    string? BlockingReason);

public sealed class MasterBindingConfigurationService
{
    private readonly InstallationIdentityResolver _installationIdentityResolver;
    private readonly MasterBindingStore _bindingStore;
    private readonly IWindowsAccountResolver _accountResolver;
    private readonly IAdministratorPrivilegeChecker _administratorPrivilegeChecker;
    private readonly ISystemClock _clock;

    public MasterBindingConfigurationService(
        InstallationIdentityResolver installationIdentityResolver,
        MasterBindingStore bindingStore,
        IWindowsAccountResolver accountResolver,
        IAdministratorPrivilegeChecker administratorPrivilegeChecker,
        ISystemClock clock)
    {
        _installationIdentityResolver = installationIdentityResolver;
        _bindingStore = bindingStore;
        _accountResolver = accountResolver;
        _administratorPrivilegeChecker = administratorPrivilegeChecker;
        _clock = clock;
    }

    public Task<MasterBindingConfigurationResult> BindCurrentUserAsync(
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        if (!_administratorPrivilegeChecker.IsElevatedAdministrator())
        {
            return Task.FromResult(Result(
                MasterBindingConfigurationStatus.AdministratorRequired,
                accountDisplayName: null,
                "Creating or replacing Master binding requires an elevated administrator shell."));
        }

        var account = _accountResolver.ResolveCurrentUser();

        return BindResolvedAccountAsync(account, replaceExisting, cancellationToken);
    }

    public Task<MasterBindingConfigurationResult> BindAccountAsync(
        string accountName,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        if (!_administratorPrivilegeChecker.IsElevatedAdministrator())
        {
            return Task.FromResult(Result(
                MasterBindingConfigurationStatus.AdministratorRequired,
                accountDisplayName: null,
                "Creating or replacing Master binding requires an elevated administrator shell."));
        }

        var account = string.IsNullOrWhiteSpace(accountName)
            ? WindowsAccountResolution.NotFound("Windows account name is required.")
            : _accountResolver.ResolveAccount(accountName);

        return BindResolvedAccountAsync(account, replaceExisting, cancellationToken);
    }

    private async Task<MasterBindingConfigurationResult> BindResolvedAccountAsync(
        WindowsAccountResolution accountResolution,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        if (!accountResolution.Found || accountResolution.Identity is null)
        {
            return Result(
                MasterBindingConfigurationStatus.WindowsAccountNotFound,
                accountDisplayName: null,
                accountResolution.ErrorMessage ?? "Windows account could not be resolved.");
        }

        var identityResolution = await _installationIdentityResolver.ResolveAsync(cancellationToken);
        if (identityResolution.Status != InstallationIdentityResolutionStatus.Ready)
        {
            return Result(
                MasterBindingConfigurationStatus.InstallationIdentityInvalid,
                accountResolution.Identity.AccountDisplayName,
                identityResolution.ErrorMessage ?? "Installation identity is invalid.");
        }

        var candidate = MasterWindowsBinding.Create(
            identityResolution.Identity!.InstallationId,
            accountResolution.Identity.WindowsSid,
            accountResolution.Identity.AccountDisplayName,
            _clock.UtcNow);

        var writeResult = await _bindingStore.WriteAsync(
            candidate,
            replaceExisting,
            cancellationToken);

        return writeResult.Status switch
        {
            MasterBindingStoreWriteStatus.Configured => Result(
                MasterBindingConfigurationStatus.Configured,
                accountResolution.Identity.AccountDisplayName,
                null),
            MasterBindingStoreWriteStatus.AlreadyConfigured => Result(
                MasterBindingConfigurationStatus.MasterBindingAlreadyConfigured,
                accountResolution.Identity.AccountDisplayName,
                writeResult.ErrorMessage),
            _ => Result(
                MasterBindingConfigurationStatus.MasterBindingInvalid,
                accountResolution.Identity.AccountDisplayName,
                writeResult.ErrorMessage ?? "Master binding could not be written.")
        };
    }

    private static MasterBindingConfigurationResult Result(
        MasterBindingConfigurationStatus status,
        string? accountDisplayName,
        string? blockingReason)
    {
        return new MasterBindingConfigurationResult(
            status,
            status == MasterBindingConfigurationStatus.Configured,
            accountDisplayName,
            blockingReason);
    }
}
