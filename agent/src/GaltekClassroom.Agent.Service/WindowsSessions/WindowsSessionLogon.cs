using GaltekClassroom.Agent.Service.CredentialProviderBridge;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using ProtoWindowsSessionState = GaltekClassroom.Protocol.Network.V1.WindowsSessionState;

namespace GaltekClassroom.Agent.Service.WindowsSessions;

public sealed record WindowsSessionLogonOptions(
    TimeSpan ActivationTtl,
    TimeSpan ProviderAvailabilityWait)
{
    public static WindowsSessionLogonOptions Default { get; } = new(
        TimeSpan.FromSeconds(45),
        TimeSpan.FromMilliseconds(750));
}

public sealed record WindowsSessionLogonServiceResult(
    bool Succeeded,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static WindowsSessionLogonServiceResult Success(string message)
    {
        return new WindowsSessionLogonServiceResult(true, NetworkOperationErrorCode.Unspecified, message);
    }

    public static WindowsSessionLogonServiceResult Failure(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return new WindowsSessionLogonServiceResult(false, errorCode, message);
    }
}

public sealed class WindowsSessionLogonService
{
    private readonly WindowsSessionStateService _sessionStateService;
    private readonly InstallationIdentityStore _installationIdentityStore;
    private readonly IManagedWindowsAccountBindingStore _bindingStore;
    private readonly IWindowsAccountResolver _accountResolver;
    private readonly IManagedWindowsCredentialStore _credentialStore;
    private readonly CredentialProviderActivationService _activationService;
    private readonly WindowsSessionLogonOptions _options;
    private readonly ILogger<WindowsSessionLogonService> _logger;

    public WindowsSessionLogonService(
        WindowsSessionStateService sessionStateService,
        InstallationIdentityStore installationIdentityStore,
        IManagedWindowsAccountBindingStore bindingStore,
        IWindowsAccountResolver accountResolver,
        IManagedWindowsCredentialStore credentialStore,
        CredentialProviderActivationService activationService,
        WindowsSessionLogonOptions options,
        ILogger<WindowsSessionLogonService> logger)
    {
        _sessionStateService = sessionStateService;
        _installationIdentityStore = installationIdentityStore;
        _bindingStore = bindingStore;
        _accountResolver = accountResolver;
        _credentialStore = credentialStore;
        _activationService = activationService;
        _options = options;
        _logger = logger;
    }

    public async Task<WindowsSessionLogonServiceResult> LogonAsync(
        string operationId,
        string accountId,
        CancellationToken cancellationToken)
    {
        WindowsSessionLogonServiceResult? sessionDecision =
            await ValidateInitialSessionAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (sessionDecision is not null)
        {
            return sessionDecision;
        }

        AccountPreflightResult accountPreflight =
            await ValidateAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!accountPreflight.Succeeded)
        {
            return Failure(accountPreflight.ErrorCode, accountPreflight.Message);
        }

        if (!await _activationService.WaitForListenerAsync(
                _options.ProviderAvailabilityWait,
                cancellationToken).ConfigureAwait(false))
        {
            return Failure(
                NetworkOperationErrorCode.CredentialProviderUnavailable,
                "Credential Provider listener is not available.");
        }

        WindowsSessionStateServiceResult second =
            await _sessionStateService.GetStateAsync(cancellationToken).ConfigureAwait(false);
        WindowsSessionLogonServiceResult? secondDecision = ValidateSecondSession(accountId, second);
        if (secondDecision is not null)
        {
            return secondDecision;
        }

        CredentialProviderActivationSetResult activationResult =
            _activationService.SetRemotePending(
                operationId,
                accountId,
                _options.ActivationTtl,
                autoSubmitRequested: true);
        if (activationResult.Status == CredentialProviderActivationSetStatus.Busy)
        {
            return Failure(
                NetworkOperationErrorCode.WindowsLogonBusy,
                activationResult.ErrorMessage ?? "Another managed Windows logon is already pending.");
        }

        if (!activationResult.Succeeded || activationResult.Activation is null)
        {
            return Failure(
                NetworkOperationErrorCode.OperationRejected,
                activationResult.ErrorMessage ?? "Credential Provider activation was rejected.");
        }

        _logger.LogInformation(
            "Managed Windows logon activation created for accountId {AccountId}.",
            accountId);

        CredentialProviderLogonCompletionOutcome outcome =
            await _activationService.WaitForCompletionAsync(
                operationId,
                activationResult.Activation.ActivationId,
                cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            CredentialProviderLogonCompletionOutcome.Success =>
                Success("Windows accepted the managed account authentication."),
            CredentialProviderLogonCompletionOutcome.Failed =>
                Failure(NetworkOperationErrorCode.WindowsLogonFailed, "Windows rejected managed account authentication."),
            CredentialProviderLogonCompletionOutcome.LocalSerializationFailed =>
                Failure(NetworkOperationErrorCode.WindowsLogonFailed, "Credential Provider serialization failed locally."),
            _ => Failure(
                NetworkOperationErrorCode.WindowsLogonNotConfirmed,
                "Windows logon was not confirmed before the activation expired.")
        };
    }

    private async Task<WindowsSessionLogonServiceResult?> ValidateInitialSessionAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        WindowsSessionStateServiceResult state =
            await _sessionStateService.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.Succeeded)
        {
            return Failure(state.ErrorCode, state.Message);
        }

        return state.State switch
        {
            ProtoWindowsSessionState.NoSession => null,
            ProtoWindowsSessionState.PrimaryActive when accountId == ClassroomManagedWindowsAccountTypes.Primary =>
                Success("Expected managed Windows session is already active."),
            ProtoWindowsSessionState.SecondaryActive when accountId == ClassroomManagedWindowsAccountTypes.Secondary =>
                Success("Expected managed Windows session is already active."),
            ProtoWindowsSessionState.PrimaryActive
                or ProtoWindowsSessionState.SecondaryActive
                or ProtoWindowsSessionState.OtherSessionActive =>
                Failure(NetworkOperationErrorCode.WindowsSessionChanged, "Another Windows session is already active."),
            _ => Failure(
                NetworkOperationErrorCode.WindowsSessionUnknown,
                "Windows console session identity could not be determined.")
        };
    }

    private static WindowsSessionLogonServiceResult? ValidateSecondSession(
        string accountId,
        WindowsSessionStateServiceResult state)
    {
        if (!state.Succeeded)
        {
            return Failure(state.ErrorCode, state.Message);
        }

        if (state.State == ProtoWindowsSessionState.NoSession)
        {
            return null;
        }

        if (state.State == ProtoWindowsSessionState.Unknown)
        {
            return Failure(
                NetworkOperationErrorCode.WindowsSessionUnknown,
                "Windows console session identity could not be determined.");
        }

        return Failure(
            NetworkOperationErrorCode.WindowsSessionChanged,
            "Windows console session changed before logon activation.");
    }

    private async Task<AccountPreflightResult> ValidateAccountAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        InstallationIdentityStoreReadResult identity =
            await _installationIdentityStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (identity.Status != InstallationIdentityStoreReadStatus.Loaded || identity.Identity is null)
        {
            return AccountPreflightResult.Failure(
                NetworkOperationErrorCode.WindowsSessionUnknown,
                identity.ErrorMessage ?? "Installation identity is invalid.");
        }

        ManagedWindowsAccountBindingStoreReadResult bindings =
            await _bindingStore.GetAsync(identity.Identity.InstallationId, accountId, cancellationToken)
                .ConfigureAwait(false);
        if (!bindings.Loaded)
        {
            return AccountPreflightResult.Failure(
                NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
                bindings.ErrorMessage ?? "Managed Windows account bindings are invalid.");
        }

        if (bindings.Bindings.Count == 0)
        {
            return AccountPreflightResult.Failure(
                NetworkOperationErrorCode.AccountNotConfigured,
                "Managed Windows account is not configured.");
        }

        ManagedWindowsAccountBinding binding = bindings.Bindings[0];
        WindowsAccountResolution resolved = _accountResolver.ResolveSid(binding.WindowsSid);
        if (!resolved.Found
            || resolved.Identity is null
            || resolved.Identity.SidNameUse != WindowsAccountSidNameUse.User
            || !string.Equals(resolved.Identity.WindowsSid, binding.WindowsSid, StringComparison.OrdinalIgnoreCase))
        {
            return AccountPreflightResult.Failure(
                NetworkOperationErrorCode.AccountNotFound,
                "Managed Windows account was not found.");
        }

        ManagedWindowsCredentialStatusResult credential =
            await _credentialStore.GetStatusAsync(
                identity.Identity.InstallationId,
                accountId,
                cancellationToken).ConfigureAwait(false);
        if (credential.Status == ManagedWindowsCredentialStatus.Usable)
        {
            return AccountPreflightResult.Success();
        }

        return AccountPreflightResult.Failure(
            CredentialErrorCode(credential.Status),
            CredentialMessage(credential.Status));
    }

    private static NetworkOperationErrorCode CredentialErrorCode(ManagedWindowsCredentialStatus status)
    {
        return status switch
        {
            ManagedWindowsCredentialStatus.CredentialNotConfigured =>
                NetworkOperationErrorCode.ManagedCredentialNotConfigured,
            ManagedWindowsCredentialStatus.AccountNotConfigured => NetworkOperationErrorCode.AccountNotConfigured,
            ManagedWindowsCredentialStatus.AccountNotFound => NetworkOperationErrorCode.AccountNotFound,
            ManagedWindowsCredentialStatus.BindingStoreInvalid => NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
            ManagedWindowsCredentialStatus.StoreInvalid => NetworkOperationErrorCode.ManagedCredentialStoreInvalid,
            _ => NetworkOperationErrorCode.ManagedCredentialStoreInvalid
        };
    }

    private static string CredentialMessage(ManagedWindowsCredentialStatus status)
    {
        return status switch
        {
            ManagedWindowsCredentialStatus.CredentialNotConfigured => "Managed Windows credential is not configured.",
            ManagedWindowsCredentialStatus.AccountNotConfigured => "Managed Windows account is not configured.",
            ManagedWindowsCredentialStatus.AccountNotFound => "Managed Windows account was not found.",
            ManagedWindowsCredentialStatus.BindingStoreInvalid => "Managed Windows account bindings are invalid.",
            ManagedWindowsCredentialStatus.StoreInvalid => "Managed Windows credential store is invalid.",
            _ => "Managed Windows credential is not usable."
        };
    }

    private static WindowsSessionLogonServiceResult Success(string message)
    {
        return WindowsSessionLogonServiceResult.Success(message);
    }

    private static WindowsSessionLogonServiceResult Failure(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return WindowsSessionLogonServiceResult.Failure(errorCode, message);
    }

    private sealed record AccountPreflightResult(
        bool Succeeded,
        NetworkOperationErrorCode ErrorCode,
        string Message)
    {
        public static AccountPreflightResult Success()
        {
            return new AccountPreflightResult(true, NetworkOperationErrorCode.Unspecified, string.Empty);
        }

        public static AccountPreflightResult Failure(
            NetworkOperationErrorCode errorCode,
            string message)
        {
            return new AccountPreflightResult(false, errorCode, message);
        }
    }
}

public sealed class LogonManagedAccountOperationHandler : IRemoteOperationHandler
{
    private readonly WindowsSessionLogonService _service;
    private readonly ILogger<LogonManagedAccountOperationHandler> _logger;

    public LogonManagedAccountOperationHandler(
        WindowsSessionLogonService service,
        ILogger<LogonManagedAccountOperationHandler> logger)
    {
        _service = service;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.LogonManagedAccount;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.LogonManagedAccount
            || request.LogonManagedAccount is null)
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "LOGON_MANAGED_ACCOUNT parameters are required.");
        }

        string? accountId = AccountIdFrom(request.LogonManagedAccount.AccountId);
        if (accountId is null)
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "Managed Windows accountId is invalid.");
        }

        WindowsSessionLogonServiceResult result =
            await _service.LogonAsync(request.OperationId, accountId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Failed(result.ErrorCode, result.Message);
        }

        _logger.LogInformation(
            "Managed Windows logon completed for accountId {AccountId}.",
            accountId);

        return RemoteOperationHandlerResult.Success(result.Message);
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

    private static RemoteOperationHandlerResult Failed(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.Failed,
            errorCode,
            message);
    }
}
