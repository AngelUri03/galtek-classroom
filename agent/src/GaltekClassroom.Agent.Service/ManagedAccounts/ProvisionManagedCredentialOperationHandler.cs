using System.Security.Cryptography;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public sealed class ProvisionManagedCredentialOperationHandler : IRemoteOperationHandler
{
    private readonly InstallationIdentityStore _installationIdentityStore;
    private readonly IManagedWindowsCredentialStore _credentialStore;
    private readonly ILogger<ProvisionManagedCredentialOperationHandler> _logger;

    public ProvisionManagedCredentialOperationHandler(
        InstallationIdentityStore installationIdentityStore,
        IManagedWindowsCredentialStore credentialStore,
        ILogger<ProvisionManagedCredentialOperationHandler> logger)
    {
        _installationIdentityStore = installationIdentityStore;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.ProvisionManagedCredential;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.ProvisionManagedCredential
            || request.ProvisionManagedCredential is null)
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "PROVISION_MANAGED_CREDENTIAL parameters are required.");
        }

        var parameters = request.ProvisionManagedCredential;
        string? accountId = AccountIdFrom(parameters.AccountId);
        if (accountId is null)
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "Managed Windows accountId is invalid.");
        }

        byte[] passwordUtf16LittleEndian = parameters.PasswordUtf16Le.ToByteArray();
        try
        {
            var validation = ValidatePasswordBytes(passwordUtf16LittleEndian);
            if (!validation.IsValid)
            {
                return Failed(
                    NetworkOperationErrorCode.ProtocolViolation,
                    validation.Message);
            }

            InstallationIdentityStoreReadResult identity =
                await _installationIdentityStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (identity.Status != InstallationIdentityStoreReadStatus.Loaded || identity.Identity is null)
            {
                return Failed(
                    NetworkOperationErrorCode.ProtocolViolation,
                    "Installation identity is invalid.");
            }

            ManagedWindowsCredentialWriteResult write =
                await _credentialStore.ReplaceUtf16LittleEndianAsync(
                    identity.Identity.InstallationId,
                    accountId,
                    passwordUtf16LittleEndian,
                    cancellationToken).ConfigureAwait(false);

            if (!write.Succeeded)
            {
                return Failed(MapWriteError(write), MessageFor(write));
            }

            _logger.LogInformation(
                "Managed credential provisioning completed for accountId {AccountId}.",
                accountId);

            return RemoteOperationHandlerResult.Success(
                "Managed credential was provisioned.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordUtf16LittleEndian);
        }
    }

    private static string? AccountIdFrom(ManagedWindowsAccountId accountId)
    {
        return accountId switch
        {
            ManagedWindowsAccountId.Primary => ClassroomManagedWindowsAccountTypes.Primary,
            ManagedWindowsAccountId.Secondary => ClassroomManagedWindowsAccountTypes.Secondary,
            _ => null
        };
    }

    private static ProvisionPasswordValidationResult ValidatePasswordBytes(byte[] passwordUtf16LittleEndian)
    {
        if (passwordUtf16LittleEndian.Length == 0)
        {
            return ProvisionPasswordValidationResult.Invalid("Managed credential password is invalid.");
        }

        if (passwordUtf16LittleEndian.Length % 2 != 0)
        {
            return ProvisionPasswordValidationResult.Invalid("Managed credential password is invalid.");
        }

        if (passwordUtf16LittleEndian.Length > ManagedWindowsCredentialConstants.MaximumPasswordCharacters * 2)
        {
            return ProvisionPasswordValidationResult.Invalid("Managed credential password is invalid.");
        }

        return ProvisionPasswordValidationResult.Valid();
    }

    private static NetworkOperationErrorCode MapWriteError(ManagedWindowsCredentialWriteResult write)
    {
        return write.Status switch
        {
            ManagedWindowsCredentialWriteStatus.AccountNotConfigured => NetworkOperationErrorCode.AccountNotConfigured,
            ManagedWindowsCredentialWriteStatus.AccountNotFound => NetworkOperationErrorCode.AccountNotFound,
            ManagedWindowsCredentialWriteStatus.BindingStoreInvalid => NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
            ManagedWindowsCredentialWriteStatus.StoreInvalid
                or ManagedWindowsCredentialWriteStatus.VerificationFailed => NetworkOperationErrorCode.ManagedCredentialStoreInvalid,
            ManagedWindowsCredentialWriteStatus.ProtectionFailed => NetworkOperationErrorCode.ManagedCredentialProtectionFailed,
            _ => NetworkOperationErrorCode.ProtocolViolation
        };
    }

    private static string MessageFor(ManagedWindowsCredentialWriteResult write)
    {
        return write.Status switch
        {
            ManagedWindowsCredentialWriteStatus.AccountNotConfigured => "Managed Windows account is not configured.",
            ManagedWindowsCredentialWriteStatus.AccountNotFound => "Managed Windows account was not found.",
            ManagedWindowsCredentialWriteStatus.BindingStoreInvalid => "Managed Windows account bindings are invalid.",
            ManagedWindowsCredentialWriteStatus.StoreInvalid
                or ManagedWindowsCredentialWriteStatus.VerificationFailed => "Managed Windows credential store is invalid.",
            ManagedWindowsCredentialWriteStatus.ProtectionFailed => "Managed Windows credential protection failed.",
            _ => "Managed credential provisioning request is invalid."
        };
    }

    private static RemoteOperationHandlerResult Failed(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.Failed,
            errorCode,
            message);
    }

    private sealed record ProvisionPasswordValidationResult(bool IsValid, string Message)
    {
        public static ProvisionPasswordValidationResult Valid()
        {
            return new ProvisionPasswordValidationResult(true, string.Empty);
        }

        public static ProvisionPasswordValidationResult Invalid(string message)
        {
            return new ProvisionPasswordValidationResult(false, message);
        }
    }
}
