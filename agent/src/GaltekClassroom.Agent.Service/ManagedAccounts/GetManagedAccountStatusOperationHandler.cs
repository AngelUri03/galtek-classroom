using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public sealed class GetManagedAccountStatusOperationHandler : IRemoteOperationHandler
{
    private static readonly string[] OrderedSlots =
    [
        ClassroomManagedWindowsAccountTypes.Primary,
        ClassroomManagedWindowsAccountTypes.Secondary,
        ClassroomManagedWindowsAccountTypes.Admin
    ];

    private readonly InstallationIdentityStore _installationIdentityStore;
    private readonly IManagedWindowsAccountBindingStore _bindingStore;
    private readonly IManagedWindowsCredentialStore _credentialStore;
    private readonly IWindowsAccountResolver _accountResolver;

    public GetManagedAccountStatusOperationHandler(
        InstallationIdentityStore installationIdentityStore,
        IManagedWindowsAccountBindingStore bindingStore,
        IManagedWindowsCredentialStore credentialStore,
        IWindowsAccountResolver accountResolver)
    {
        _installationIdentityStore = installationIdentityStore;
        _bindingStore = bindingStore;
        _credentialStore = credentialStore;
        _accountResolver = accountResolver;
    }

    public NetworkOperationType OperationType => NetworkOperationType.GetManagedAccountStatus;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.None)
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "GET_MANAGED_ACCOUNT_STATUS does not accept parameters.");
        }

        InstallationIdentityStoreReadResult identity =
            await _installationIdentityStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (identity.Status != InstallationIdentityStoreReadStatus.Loaded || identity.Identity is null)
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "Installation identity is invalid.");
        }

        ManagedWindowsAccountBindingStoreReadResult bindings =
            await _bindingStore.ListAsync(identity.Identity.InstallationId, cancellationToken).ConfigureAwait(false);
        if (!bindings.Loaded)
        {
            return Failed(
                NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
                "Managed Windows account bindings are invalid.");
        }

        var byAccountId = bindings.Bindings.ToDictionary(
            binding => binding.AccountId,
            StringComparer.Ordinal);
        var result = new ManagedAccountStatusResult();
        try
        {
            foreach (string accountId in OrderedSlots)
            {
                result.Accounts.Add(await BuildSlotStatusAsync(
                    identity.Identity.InstallationId,
                    accountId,
                    byAccountId,
                    cancellationToken).ConfigureAwait(false));
            }
        }
        catch (StatusReadException exception)
        {
            return Failed(exception.ErrorCode, exception.Message);
        }

        return RemoteOperationHandlerResult.Success(
            "Managed account status was read.",
            result);
    }

    private async Task<ManagedAccountStatus> BuildSlotStatusAsync(
        Guid installationId,
        string accountId,
        IReadOnlyDictionary<string, ManagedWindowsAccountBinding> bindings,
        CancellationToken cancellationToken)
    {
        ManagedWindowsAccountId networkAccountId = ToNetworkAccountId(accountId);
        if (!bindings.TryGetValue(accountId, out ManagedWindowsAccountBinding? binding))
        {
            return new ManagedAccountStatus
            {
                AccountId = networkAccountId,
                Configured = false,
                CredentialConfigured = false,
                CredentialStatus = ManagedAccountCredentialStatus.NotConfigured,
                WindowsAccountName = string.Empty
            };
        }

        WindowsAccountResolution resolved = _accountResolver.ResolveSid(binding.WindowsSid);
        bool foundUser = resolved.Found
            && resolved.Identity is not null
            && resolved.Identity.SidNameUse == WindowsAccountSidNameUse.User
            && string.Equals(resolved.Identity.WindowsSid, binding.WindowsSid, StringComparison.OrdinalIgnoreCase);
        if (!foundUser)
        {
            return new ManagedAccountStatus
            {
                AccountId = networkAccountId,
                Configured = true,
                CredentialConfigured = false,
                CredentialStatus = ManagedAccountCredentialStatus.AccountNotFound,
                WindowsAccountName = binding.AccountReference
            };
        }

        ManagedWindowsCredentialStatusResult credential =
            await _credentialStore.GetStatusAsync(installationId, accountId, cancellationToken).ConfigureAwait(false);
        if (credential.Status is ManagedWindowsCredentialStatus.BindingStoreInvalid)
        {
            throw new StatusReadException(
                NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
                "Managed Windows account bindings are invalid.");
        }
        if (credential.Status is ManagedWindowsCredentialStatus.StoreInvalid or ManagedWindowsCredentialStatus.Invalid)
        {
            throw new StatusReadException(
                NetworkOperationErrorCode.ManagedCredentialStoreInvalid,
                "Managed Windows credential store is invalid.");
        }

        return new ManagedAccountStatus
        {
            AccountId = networkAccountId,
            Configured = true,
            CredentialConfigured = credential.CredentialConfigured,
            CredentialStatus = ToCredentialStatus(credential.Status),
            WindowsAccountName = resolved.Identity!.AccountDisplayName
        };
    }

    private static ManagedAccountCredentialStatus ToCredentialStatus(ManagedWindowsCredentialStatus status)
    {
        return status switch
        {
            ManagedWindowsCredentialStatus.Usable => ManagedAccountCredentialStatus.Ready,
            ManagedWindowsCredentialStatus.CredentialNotConfigured => ManagedAccountCredentialStatus.CredentialNotConfigured,
            ManagedWindowsCredentialStatus.AccountNotConfigured => ManagedAccountCredentialStatus.NotConfigured,
            ManagedWindowsCredentialStatus.AccountNotFound => ManagedAccountCredentialStatus.AccountNotFound,
            _ => ManagedAccountCredentialStatus.CredentialNotConfigured
        };
    }

    private static ManagedWindowsAccountId ToNetworkAccountId(string accountId)
    {
        return accountId == ClassroomManagedWindowsAccountTypes.Primary
            ? ManagedWindowsAccountId.Primary
            : accountId == ClassroomManagedWindowsAccountTypes.Secondary
                ? ManagedWindowsAccountId.Secondary
                : ManagedWindowsAccountId.Admin;
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

    private sealed class StatusReadException : Exception
    {
        public StatusReadException(NetworkOperationErrorCode errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public NetworkOperationErrorCode ErrorCode { get; }
    }
}
