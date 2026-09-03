using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Applications;

public enum ApplicationBindingConfigurationStatus
{
    Success,
    AdministratorRequired,
    ApplicationBindingsInvalid,
    ApplicationBindingNotFound,
    ApplicationBindingAlreadyExists,
    ApplicationBindingInvalid,
    ApplicationExecutableNotFound
}

public static class ApplicationBindingConfigurationStatusExtensions
{
    public static string ToCode(this ApplicationBindingConfigurationStatus status)
    {
        return status switch
        {
            ApplicationBindingConfigurationStatus.Success => "SUCCESS",
            ApplicationBindingConfigurationStatus.AdministratorRequired => ApplicationBindingErrorCodes.AdministratorRequired,
            ApplicationBindingConfigurationStatus.ApplicationBindingsInvalid => ApplicationBindingErrorCodes.ApplicationBindingsInvalid,
            ApplicationBindingConfigurationStatus.ApplicationBindingNotFound => ApplicationBindingErrorCodes.ApplicationBindingNotFound,
            ApplicationBindingConfigurationStatus.ApplicationBindingAlreadyExists => ApplicationBindingErrorCodes.ApplicationBindingAlreadyExists,
            ApplicationBindingConfigurationStatus.ApplicationBindingInvalid => ApplicationBindingErrorCodes.ApplicationBindingInvalid,
            ApplicationBindingConfigurationStatus.ApplicationExecutableNotFound => ApplicationBindingErrorCodes.ApplicationExecutableNotFound,
            _ => ApplicationBindingErrorCodes.ApplicationBindingInvalid
        };
    }
}

public sealed record ApplicationBindingConfigurationResult(
    ApplicationBindingConfigurationStatus Status,
    bool Succeeded,
    IReadOnlyList<ApplicationBinding> Bindings,
    string? BlockingReason);

public sealed class ApplicationBindingConfigurationService
{
    private readonly IApplicationBindingStore _store;
    private readonly IAdministratorPrivilegeChecker _administratorPrivilegeChecker;

    public ApplicationBindingConfigurationService(
        IApplicationBindingStore store,
        IAdministratorPrivilegeChecker administratorPrivilegeChecker)
    {
        _store = store;
        _administratorPrivilegeChecker = administratorPrivilegeChecker;
    }

    public async Task<ApplicationBindingConfigurationResult> ListAsync(CancellationToken cancellationToken)
    {
        var result = await _store.ListAsync(cancellationToken).ConfigureAwait(false);

        return result.Loaded
            ? Success(result.Bindings)
            : Failure(
                ApplicationBindingConfigurationStatus.ApplicationBindingsInvalid,
                result.ErrorMessage ?? "Application bindings are invalid.");
    }

    public Task<ApplicationBindingConfigurationResult> BindAppPathAsync(
        string applicationId,
        string appPathExecutableName,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        return MutateAsync(
            token => _store.AddAppPathAsync(applicationId, appPathExecutableName, replaceExisting, token),
            cancellationToken);
    }

    public Task<ApplicationBindingConfigurationResult> BindAbsoluteExeAsync(
        string applicationId,
        string executablePath,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        return MutateAsync(
            token => _store.AddAbsoluteExeAsync(applicationId, executablePath, replaceExisting, token),
            cancellationToken);
    }

    public Task<ApplicationBindingConfigurationResult> SetEnabledAsync(
        string applicationId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        return MutateAsync(
            token => _store.SetEnabledAsync(applicationId, enabled, token),
            cancellationToken);
    }

    public Task<ApplicationBindingConfigurationResult> RemoveAsync(
        string applicationId,
        CancellationToken cancellationToken)
    {
        return MutateAsync(
            token => _store.RemoveAsync(applicationId, token),
            cancellationToken);
    }

    private async Task<ApplicationBindingConfigurationResult> MutateAsync(
        Func<CancellationToken, Task<ApplicationBindingStoreWriteResult>> mutation,
        CancellationToken cancellationToken)
    {
        if (!_administratorPrivilegeChecker.IsElevatedAdministrator())
        {
            return Failure(
                ApplicationBindingConfigurationStatus.AdministratorRequired,
                "Changing application bindings requires an elevated administrator shell.");
        }

        var result = await mutation(cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            var list = await _store.ListAsync(cancellationToken).ConfigureAwait(false);

            return list.Loaded
                ? Success(list.Bindings)
                : Failure(
                    ApplicationBindingConfigurationStatus.ApplicationBindingsInvalid,
                    list.ErrorMessage ?? "Application bindings are invalid.");
        }

        return Failure(ToConfigurationStatus(result.Status), result.ErrorMessage ?? "Application binding change failed.");
    }

    private static ApplicationBindingConfigurationStatus ToConfigurationStatus(
        ApplicationBindingStoreWriteStatus status)
    {
        return status switch
        {
            ApplicationBindingStoreWriteStatus.AlreadyExists => ApplicationBindingConfigurationStatus.ApplicationBindingAlreadyExists,
            ApplicationBindingStoreWriteStatus.NotFound => ApplicationBindingConfigurationStatus.ApplicationBindingNotFound,
            ApplicationBindingStoreWriteStatus.ExecutableNotFound => ApplicationBindingConfigurationStatus.ApplicationExecutableNotFound,
            ApplicationBindingStoreWriteStatus.StoreInvalid => ApplicationBindingConfigurationStatus.ApplicationBindingsInvalid,
            ApplicationBindingStoreWriteStatus.VerificationFailed => ApplicationBindingConfigurationStatus.ApplicationBindingsInvalid,
            _ => ApplicationBindingConfigurationStatus.ApplicationBindingInvalid
        };
    }

    private static ApplicationBindingConfigurationResult Success(IReadOnlyList<ApplicationBinding> bindings)
    {
        return new ApplicationBindingConfigurationResult(
            ApplicationBindingConfigurationStatus.Success,
            true,
            bindings,
            null);
    }

    private static ApplicationBindingConfigurationResult Failure(
        ApplicationBindingConfigurationStatus status,
        string blockingReason)
    {
        return new ApplicationBindingConfigurationResult(
            status,
            false,
            Array.Empty<ApplicationBinding>(),
            blockingReason);
    }
}
