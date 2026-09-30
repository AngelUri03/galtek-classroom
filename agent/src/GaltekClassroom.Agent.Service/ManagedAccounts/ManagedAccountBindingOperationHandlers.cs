using System.ComponentModel;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.WindowsAccounts;
using GaltekClassroom.Agent.Service.WindowsSessions;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public sealed class ManagedAccountBindingMutationService
{
    private readonly InstallationIdentityStore _installationIdentityStore;
    private readonly IManagedWindowsAccountBindingStore _bindingStore;
    private readonly IManagedWindowsCredentialStore _credentialStore;
    private readonly IWindowsAccountInventorySource _inventorySource;
    private readonly IWindowsConsoleSessionResolver _sessionResolver;
    private readonly ISystemClock _clock;
    private readonly ILogger<ManagedAccountBindingMutationService> _logger;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public ManagedAccountBindingMutationService(
        InstallationIdentityStore installationIdentityStore,
        IManagedWindowsAccountBindingStore bindingStore,
        IManagedWindowsCredentialStore credentialStore,
        IWindowsAccountInventorySource inventorySource,
        IWindowsConsoleSessionResolver sessionResolver,
        ISystemClock clock,
        ILogger<ManagedAccountBindingMutationService> logger)
    {
        _installationIdentityStore = installationIdentityStore;
        _bindingStore = bindingStore;
        _credentialStore = credentialStore;
        _inventorySource = inventorySource;
        _sessionResolver = sessionResolver;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RemoteOperationHandlerResult> BindAsync(
        ManagedWindowsAccountId networkRole,
        string windowsAccountName,
        string deviceId,
        CancellationToken cancellationToken)
    {
        string? role = RoleFrom(networkRole);
        if (role is null || string.IsNullOrWhiteSpace(windowsAccountName))
        {
            return Failed(NetworkOperationErrorCode.ManagedAccountBindingInvalid,
                "Managed role and Windows account name are required.");
        }

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InstallationIdentityStoreReadResult identity =
                await _installationIdentityStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (identity.Status != InstallationIdentityStoreReadStatus.Loaded || identity.Identity is null)
            {
                return Failed(NetworkOperationErrorCode.ProtocolViolation, "Installation identity is invalid.");
            }

            IReadOnlyList<WindowsLocalAccountRecord> inventory;
            try
            {
                inventory = _inventorySource.Read(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is Win32Exception
                or InvalidOperationException
                or PlatformNotSupportedException)
            {
                return Failed(NetworkOperationErrorCode.OperationRejected,
                    "Windows account inventory could not be read.");
            }

            WindowsLocalAccountRecord? account = inventory.SingleOrDefault(candidate =>
                string.Equals(candidate.AccountName, windowsAccountName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return Failed(NetworkOperationErrorCode.AccountNotFound,
                    "The selected Windows account no longer exists.");
            }
            if (!account.Enabled)
            {
                return Failed(NetworkOperationErrorCode.WindowsAccountDisabled,
                    "The selected Windows account is disabled.");
            }
            if (account.BuiltIn)
            {
                return Failed(NetworkOperationErrorCode.WindowsAccountBuiltIn,
                    "Built-in Windows accounts cannot be managed by Galtek.");
            }
            if (role == ClassroomManagedWindowsAccountTypes.Admin && !account.Administrator)
            {
                return Failed(NetworkOperationErrorCode.WindowsAccountAdminRequired,
                    "ADMIN requires current membership in the Windows Administrators group.");
            }
            if (role != ClassroomManagedWindowsAccountTypes.Admin && account.Administrator)
            {
                return Failed(NetworkOperationErrorCode.WindowsAccountAdminNotAllowed,
                    "PRIMARY and SECONDARY cannot use an administrative Windows account.");
            }

            ManagedWindowsAccountBindingStoreReadResult current = await _bindingStore.ListAsync(
                identity.Identity.InstallationId,
                cancellationToken).ConfigureAwait(false);
            if (!current.Loaded)
            {
                return Failed(NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
                    "Managed Windows account bindings are invalid.");
            }
            ManagedWindowsAccountBinding? existingRole = current.Bindings.SingleOrDefault(binding =>
                string.Equals(binding.AccountId, role, StringComparison.Ordinal));
            if (current.Bindings.Any(binding => string.Equals(
                binding.AccountId,
                role,
                StringComparison.Ordinal) is false && string.Equals(
                binding.WindowsSid,
                account.WindowsSid,
                StringComparison.OrdinalIgnoreCase)))
            {
                return Failed(NetworkOperationErrorCode.WindowsAccountAlreadyManaged,
                    "The selected Windows account is already assigned to another Galtek role.");
            }

            if (existingRole is not null
                && string.Equals(existingRole.WindowsSid, account.WindowsSid, StringComparison.OrdinalIgnoreCase))
            {
                return RemoteOperationHandlerResult.Success("Managed Windows account binding is already current.");
            }

            if (existingRole is not null)
            {
                ConsoleSessionIdentityObservation session =
                    await _sessionResolver.ObserveAsync(cancellationToken).ConfigureAwait(false);
                if (!session.Succeeded || session.Status == ConsoleSessionIdentityObservationStatus.Unknown)
                {
                    return Failed(NetworkOperationErrorCode.WindowsSessionUnknown,
                        "Windows session state could not be confirmed before replacing the binding.");
                }
                if (session.Status == ConsoleSessionIdentityObservationStatus.User
                    && string.Equals(session.WindowsSid, existingRole.WindowsSid, StringComparison.OrdinalIgnoreCase))
                {
                    return Failed(NetworkOperationErrorCode.ManagedAccountSessionActive,
                        "The managed profile cannot be changed while its Windows session is active.");
                }
            }

            ManagedWindowsAccountBinding candidate = ManagedWindowsAccountBinding.Create(
                role,
                account.WindowsSid,
                account.AccountName,
                _clock.UtcNow);
            ManagedWindowsAccountBindingStoreWriteResult write = existingRole is null
                ? await _bindingStore.AddAsync(
                    identity.Identity.InstallationId,
                    candidate,
                    cancellationToken).ConfigureAwait(false)
                : await _bindingStore.ReplaceAsync(
                    identity.Identity.InstallationId,
                    candidate,
                    cancellationToken).ConfigureAwait(false);
            if (!write.Succeeded)
            {
                return Failed(MapBindingError(write),
                    write.ErrorMessage ?? "Managed account binding failed.");
            }

            if (existingRole is not null)
            {
                ManagedWindowsCredentialWriteResult cleanup = await _credentialStore.RemoveAsync(
                    identity.Identity.InstallationId,
                    role,
                    cancellationToken).ConfigureAwait(false);
                if (!cleanup.Succeeded && cleanup.Status != ManagedWindowsCredentialWriteStatus.NotFound)
                {
                    ManagedWindowsAccountBindingStoreWriteResult rollback = await _bindingStore.ReplaceAsync(
                        identity.Identity.InstallationId,
                        existingRole,
                        cancellationToken).ConfigureAwait(false);
                    return Failed(NetworkOperationErrorCode.ManagedCredentialCleanupFailed,
                        rollback.Succeeded
                            ? "Protected credential cleanup failed; the previous binding was restored."
                            : "Protected credential cleanup failed and binding recovery requires attention.");
                }
            }

            _logger.LogInformation(
                "MANAGED_ACCOUNT_BOUND DeviceId: {DeviceId}; Role: {Role}; AccountName: {AccountName}; Result: SUCCESS",
                deviceId,
                role,
                account.AccountName);
            return RemoteOperationHandlerResult.Success(existingRole is null
                ? "Managed Windows account was bound."
                : "Managed Windows account binding was replaced; a new credential is required.");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<RemoteOperationHandlerResult> UnbindAsync(
        ManagedWindowsAccountId networkRole,
        string deviceId,
        CancellationToken cancellationToken)
    {
        string? role = RoleFrom(networkRole);
        if (role is null)
        {
            return Failed(NetworkOperationErrorCode.ManagedAccountBindingInvalid,
                "Managed role is invalid.");
        }

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InstallationIdentityStoreReadResult identity =
                await _installationIdentityStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (identity.Status != InstallationIdentityStoreReadStatus.Loaded || identity.Identity is null)
            {
                return Failed(NetworkOperationErrorCode.ProtocolViolation, "Installation identity is invalid.");
            }

            ManagedWindowsAccountBindingStoreReadResult current = await _bindingStore.ListAsync(
                identity.Identity.InstallationId,
                cancellationToken).ConfigureAwait(false);
            if (!current.Loaded)
            {
                return Failed(NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
                    "Managed Windows account bindings are invalid.");
            }
            ManagedWindowsAccountBinding? existing = current.Bindings.SingleOrDefault(binding =>
                string.Equals(binding.AccountId, role, StringComparison.Ordinal));
            if (existing is null)
            {
                return Failed(NetworkOperationErrorCode.AccountNotConfigured,
                    "Managed Windows account is not configured.");
            }

            ConsoleSessionIdentityObservation session =
                await _sessionResolver.ObserveAsync(cancellationToken).ConfigureAwait(false);
            if (!session.Succeeded || session.Status == ConsoleSessionIdentityObservationStatus.Unknown)
            {
                return Failed(NetworkOperationErrorCode.WindowsSessionUnknown,
                    "Windows session state could not be confirmed before removing the binding.");
            }
            if (session.Status == ConsoleSessionIdentityObservationStatus.User
                && string.Equals(session.WindowsSid, existing.WindowsSid, StringComparison.OrdinalIgnoreCase))
            {
                return Failed(NetworkOperationErrorCode.ManagedAccountSessionActive,
                    "The managed profile cannot be removed while its Windows session is active.");
            }

            ManagedWindowsAccountBindingStoreWriteResult removed = await _bindingStore.RemoveAsync(
                identity.Identity.InstallationId,
                role,
                cancellationToken).ConfigureAwait(false);
            if (!removed.Succeeded)
            {
                return Failed(MapBindingError(removed),
                    removed.ErrorMessage ?? "Managed account binding could not be removed.");
            }

            ManagedWindowsCredentialWriteResult credential = await _credentialStore.RemoveAsync(
                identity.Identity.InstallationId,
                role,
                cancellationToken).ConfigureAwait(false);
            bool credentialAbsent = credential.Status == ManagedWindowsCredentialWriteStatus.NotFound;
            if (!credential.Succeeded && !credentialAbsent)
            {
                ManagedWindowsAccountBindingStoreWriteResult compensated = await _bindingStore.AddAsync(
                    identity.Identity.InstallationId,
                    existing,
                    cancellationToken).ConfigureAwait(false);
                _logger.LogWarning(
                    "MANAGED_ACCOUNT_UNBOUND DeviceId: {DeviceId}; Role: {Role}; Result: FAILED; ErrorCode: {ErrorCode}; Compensated: {Compensated}",
                    deviceId,
                    role,
                    ManagedWindowsAccountBindingErrorCodes.ManagedCredentialCleanupFailed,
                    compensated.Succeeded);
                return Failed(NetworkOperationErrorCode.ManagedCredentialCleanupFailed,
                    compensated.Succeeded
                        ? "Credential cleanup failed; the managed binding was restored."
                        : "Credential cleanup and managed binding compensation failed.");
            }

            _logger.LogInformation(
                "MANAGED_ACCOUNT_UNBOUND DeviceId: {DeviceId}; Role: {Role}; AccountName: {AccountName}; Result: SUCCESS",
                deviceId,
                role,
                existing.AccountReference);
            return RemoteOperationHandlerResult.Success("Managed Windows account and protected credential were removed.");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public static string? RoleFrom(ManagedWindowsAccountId role)
    {
        return role switch
        {
            ManagedWindowsAccountId.Primary => ClassroomManagedWindowsAccountTypes.Primary,
            ManagedWindowsAccountId.Secondary => ClassroomManagedWindowsAccountTypes.Secondary,
            ManagedWindowsAccountId.Admin => ClassroomManagedWindowsAccountTypes.Admin,
            _ => null
        };
    }

    private static NetworkOperationErrorCode MapBindingError(ManagedWindowsAccountBindingStoreWriteResult write)
    {
        return write.Status switch
        {
            ManagedWindowsAccountBindingStoreWriteStatus.AlreadyExists => NetworkOperationErrorCode.ManagedRoleAlreadyAssigned,
            ManagedWindowsAccountBindingStoreWriteStatus.Conflict => NetworkOperationErrorCode.ManagedAccountBindingConflict,
            ManagedWindowsAccountBindingStoreWriteStatus.StoreInvalid
                or ManagedWindowsAccountBindingStoreWriteStatus.VerificationFailed => NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
            ManagedWindowsAccountBindingStoreWriteStatus.NotFound => NetworkOperationErrorCode.AccountNotConfigured,
            _ => NetworkOperationErrorCode.ManagedAccountBindingInvalid
        };
    }

    private static RemoteOperationHandlerResult Failed(NetworkOperationErrorCode code, string message)
    {
        return new RemoteOperationHandlerResult(OperationExecutionStatus.Failed, code, message);
    }
}

public sealed class SetManagedAccountBindingOperationHandler : IRemoteOperationHandler
{
    private readonly ManagedAccountBindingMutationService _service;

    public SetManagedAccountBindingOperationHandler(ManagedAccountBindingMutationService service)
    {
        _service = service;
    }

    public NetworkOperationType OperationType => NetworkOperationType.SetManagedAccountBinding;

    public Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.SetManagedAccountBinding
            || request.SetManagedAccountBinding is null)
        {
            return Task.FromResult(new RemoteOperationHandlerResult(
                OperationExecutionStatus.Failed,
                NetworkOperationErrorCode.ProtocolViolation,
                "SET_MANAGED_ACCOUNT_BINDING parameters are required."));
        }

        return _service.BindAsync(
            request.SetManagedAccountBinding.AccountId,
            request.SetManagedAccountBinding.WindowsAccountName,
            request.TargetDeviceId,
            cancellationToken);
    }
}

public sealed class RemoveManagedAccountBindingOperationHandler : IRemoteOperationHandler
{
    private readonly ManagedAccountBindingMutationService _service;

    public RemoveManagedAccountBindingOperationHandler(ManagedAccountBindingMutationService service)
    {
        _service = service;
    }

    public NetworkOperationType OperationType => NetworkOperationType.RemoveManagedAccountBinding;

    public Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.RemoveManagedAccountBinding
            || request.RemoveManagedAccountBinding is null)
        {
            return Task.FromResult(new RemoteOperationHandlerResult(
                OperationExecutionStatus.Failed,
                NetworkOperationErrorCode.ProtocolViolation,
                "REMOVE_MANAGED_ACCOUNT_BINDING parameters are required."));
        }

        return _service.UnbindAsync(
            request.RemoveManagedAccountBinding.AccountId,
            request.TargetDeviceId,
            cancellationToken);
    }
}
