using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public sealed class RemoveManagedCredentialOperationHandler : IRemoteOperationHandler
{
    private readonly InstallationIdentityStore _installationIdentityStore;
    private readonly IManagedWindowsAccountBindingStore _bindingStore;
    private readonly IManagedWindowsCredentialStore _credentialStore;
    private readonly ILogger<RemoveManagedCredentialOperationHandler> _logger;

    public RemoveManagedCredentialOperationHandler(
        InstallationIdentityStore installationIdentityStore,
        IManagedWindowsAccountBindingStore bindingStore,
        IManagedWindowsCredentialStore credentialStore,
        ILogger<RemoveManagedCredentialOperationHandler> logger)
    {
        _installationIdentityStore = installationIdentityStore;
        _bindingStore = bindingStore;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.RemoveManagedCredential;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.RemoveManagedCredential
            || request.RemoveManagedCredential is null)
        {
            return Failed(NetworkOperationErrorCode.ProtocolViolation,
                "REMOVE_MANAGED_CREDENTIAL parameters are required.");
        }

        string? accountId = AccountIdFrom(request.RemoveManagedCredential.AccountId);
        if (accountId is null)
        {
            return Failed(NetworkOperationErrorCode.ProtocolViolation,
                "Managed Windows accountId is invalid.");
        }

        InstallationIdentityStoreReadResult identity =
            await _installationIdentityStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (identity.Status != InstallationIdentityStoreReadStatus.Loaded || identity.Identity is null)
        {
            return Failed(NetworkOperationErrorCode.ProtocolViolation, "Installation identity is invalid.");
        }

        ManagedWindowsAccountBindingStoreReadResult binding = await _bindingStore.GetAsync(
            identity.Identity.InstallationId, accountId, cancellationToken).ConfigureAwait(false);
        if (!binding.Loaded)
        {
            return Failed(NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
                binding.ErrorMessage ?? "Managed Windows account bindings are invalid.");
        }
        if (binding.Bindings.Count == 0)
        {
            return Failed(NetworkOperationErrorCode.AccountNotConfigured,
                "Managed Windows account is not configured.");
        }

        ManagedWindowsCredentialWriteResult removed = await _credentialStore.RemoveAsync(
            identity.Identity.InstallationId, accountId, cancellationToken).ConfigureAwait(false);
        if (!removed.Succeeded && removed.Status != ManagedWindowsCredentialWriteStatus.NotFound)
        {
            return Failed(NetworkOperationErrorCode.ManagedCredentialStoreInvalid,
                "Managed Windows credential could not be removed.");
        }

        _logger.LogInformation(
            "MANAGED_ACCOUNT_CREDENTIAL_REMOVED DeviceId: {DeviceId}; Role: {Role}; Result: SUCCESS",
            request.TargetDeviceId,
            accountId);
        return RemoteOperationHandlerResult.Success(
            removed.Status == ManagedWindowsCredentialWriteStatus.NotFound
                ? "Managed credential was already absent."
                : "Managed credential was removed.");
    }

    private static string? AccountIdFrom(ManagedWindowsAccountId accountId) => accountId switch
    {
        ManagedWindowsAccountId.Primary => ClassroomManagedWindowsAccountTypes.Primary,
        ManagedWindowsAccountId.Secondary => ClassroomManagedWindowsAccountTypes.Secondary,
        ManagedWindowsAccountId.Admin => ClassroomManagedWindowsAccountTypes.Admin,
        _ => null
    };

    private static RemoteOperationHandlerResult Failed(NetworkOperationErrorCode errorCode, string message) =>
        new(OperationExecutionStatus.Failed, errorCode, message);
}
