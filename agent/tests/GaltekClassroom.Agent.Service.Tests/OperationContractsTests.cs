using System.Reflection;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class OperationContractsTests
{
    [Fact]
    public void OperationTypes_ExposeTypedClassroomActions()
    {
        var operations = ConstantValues(typeof(ClassroomOperationTypes));

        Assert.Contains(ClassroomOperationTypes.LockInput, operations);
        Assert.Contains(ClassroomOperationTypes.UnlockInput, operations);
        Assert.Contains(ClassroomOperationTypes.Shutdown, operations);
        Assert.Contains(ClassroomOperationTypes.Restart, operations);
        Assert.Contains(ClassroomOperationTypes.OpenApplication, operations);
        Assert.Contains(ClassroomOperationTypes.OpenUrl, operations);
        Assert.Contains(ClassroomOperationTypes.DistributeFile, operations);
        Assert.Contains(ClassroomOperationTypes.MoveStudent, operations);
        Assert.Contains(ClassroomOperationTypes.SwapStudents, operations);
        Assert.Contains(ClassroomOperationTypes.GetWindowsSessionState, operations);
        Assert.Contains(ClassroomOperationTypes.LogonManagedAccount, operations);
        Assert.Contains(ClassroomOperationTypes.LogoffWindowsSession, operations);
        Assert.Contains(ClassroomOperationTypes.SwitchManagedAccount, operations);
        Assert.Contains(ClassroomOperationTypes.ProvisionManagedCredential, operations);
        Assert.Contains(ClassroomOperationTypes.ApplyBrowserNavigationPolicy, operations);
        Assert.Contains(ClassroomOperationTypes.ApplyBrowserDownloadPolicy, operations);
    }

    [Fact]
    public void OperationTypes_DoNotExposeArbitraryShellExecution()
    {
        var operations = ConstantValues(typeof(ClassroomOperationTypes));

        Assert.DoesNotContain("EXECUTE_COMMAND", operations);
        Assert.DoesNotContain("RUN_COMMAND", operations);
        Assert.DoesNotContain("RUN_POWERSHELL", operations);
        Assert.DoesNotContain("RUN_CMD", operations);
        Assert.DoesNotContain("EXECUTE_PATH", operations);
        Assert.DoesNotContain("SET_PASSWORD", operations);
        Assert.DoesNotContain("EXECUTE_CREDENTIAL", operations);
        Assert.DoesNotContain("RUN_LOGON", operations);
        Assert.DoesNotContain("UPDATE_WINDOWS_PASSWORD", operations);
        Assert.DoesNotContain("CHANGE_ACCOUNT_PASSWORD", operations);
    }

    [Fact]
    public void WorkspaceDestinations_AreLogicalDestinations()
    {
        var destinations = ConstantValues(typeof(ClassroomWorkspaceDestinations));

        Assert.Contains(ClassroomWorkspaceDestinations.WorkspaceRoot, destinations);
        Assert.Contains(ClassroomWorkspaceDestinations.Homework, destinations);
        Assert.Contains(ClassroomWorkspaceDestinations.ClassroomShared, destinations);
        Assert.Contains(ClassroomWorkspaceDestinations.RemovableStorage, destinations);
        Assert.DoesNotContain(@"C:\Windows\System32", destinations);
        Assert.DoesNotContain("..\\..\\Windows", destinations);
    }

    [Fact]
    public void OperationalModelContracts_ExposeClassroomReadinessWorkspaceProjectionAndPriorityNames()
    {
        var assignmentStrategies = ConstantValues(typeof(ClassroomStudentAssignmentStrategies));
        var preparationStages = ConstantValues(typeof(ClassroomStudentPreparationStages));
        var preparationStatuses = ConstantValues(typeof(ClassroomStudentPreparationStatuses));
        var residencyStates = ConstantValues(typeof(ClassroomWorkspaceResidencyStates));
        var syncStates = ConstantValues(typeof(ClassroomWorkspaceSyncStates));
        var projectionModes = ConstantValues(typeof(ClassroomProjectionModes));
        var priorities = ConstantValues(typeof(ClassroomOperationPriorities));

        Assert.Contains(ClassroomStudentAssignmentStrategies.ListOrder, assignmentStrategies);
        Assert.Contains(ClassroomStudentAssignmentStrategies.Random, assignmentStrategies);
        Assert.Contains(ClassroomStudentAssignmentStrategies.Previous, assignmentStrategies);
        Assert.Contains(ClassroomStudentAssignmentStrategies.Manual, assignmentStrategies);
        Assert.Contains(ClassroomStudentPreparationStages.PreparingWindowsSession, preparationStages);
        Assert.Contains(ClassroomStudentPreparationStages.Ready, preparationStages);
        Assert.Contains(ClassroomStudentPreparationStatuses.PartialReady, preparationStatuses);
        Assert.Contains(ClassroomWorkspaceResidencyStates.NotMaterialized, residencyStates);
        Assert.Contains(ClassroomWorkspaceSyncStates.DirtyLocal, syncStates);
        Assert.Contains(ClassroomWorkspaceSyncStates.PendingSync, syncStates);
        Assert.Contains(ClassroomWorkspaceSyncStates.Conflict, syncStates);
        Assert.Contains(ClassroomProjectionModes.ScreenShare, projectionModes);
        Assert.Contains(ClassroomProjectionModes.OpenWebContent, projectionModes);
        Assert.Contains(ClassroomOperationPriorities.Critical, priorities);
        Assert.Contains(ClassroomOperationPriorities.Low, priorities);
    }

    [Fact]
    public void ManagedWindowsAccountContracts_ExposeLogicalIdsAndSessionStatesOnly()
    {
        var accountTypes = ConstantValues(typeof(ClassroomManagedWindowsAccountTypes));
        var accountStatuses = ConstantValues(typeof(ClassroomManagedWindowsAccountStatuses));
        var sessionStates = ConstantValues(typeof(ClassroomWindowsSessionStates));
        var switchActions = ConstantValues(typeof(ClassroomManagedAccountSwitchActions));

        Assert.Contains(ClassroomManagedWindowsAccountTypes.Primary, accountTypes);
        Assert.Contains(ClassroomManagedWindowsAccountTypes.Secondary, accountTypes);
        Assert.DoesNotContain("Administrator", accountTypes);
        Assert.Contains(ClassroomManagedWindowsAccountStatuses.NotConfigured, accountStatuses);
        Assert.Contains(ClassroomManagedWindowsAccountStatuses.CredentialNotConfigured, accountStatuses);
        Assert.Contains(ClassroomManagedWindowsAccountStatuses.AccountNotFound, accountStatuses);
        Assert.Contains(ClassroomWindowsSessionStates.NoSession, sessionStates);
        Assert.Contains(ClassroomWindowsSessionStates.PrimaryActive, sessionStates);
        Assert.Contains(ClassroomWindowsSessionStates.SecondaryActive, sessionStates);
        Assert.Contains(ClassroomWindowsSessionStates.OtherSessionActive, sessionStates);
        Assert.Contains(ClassroomWindowsSessionStates.Unknown, sessionStates);
        Assert.Contains(ClassroomManagedAccountSwitchActions.NoChange, switchActions);
        Assert.Contains(ClassroomManagedAccountSwitchActions.Logon, switchActions);
        Assert.Contains(ClassroomManagedAccountSwitchActions.Switch, switchActions);
    }

    [Fact]
    public void PerformanceContracts_ExposeOperationalProfilesWithoutAuthorizationMeaning()
    {
        var clientProfiles = ConstantValues(typeof(ClassroomDevicePerformanceProfiles));
        var masterProfiles = ConstantValues(typeof(ClassroomMasterPerformanceProfiles));
        var workClasses = ConstantValues(typeof(ClassroomResourceWorkClasses));
        var pressureStates = ConstantValues(typeof(ClassroomResourcePressureStates));
        var sheddableWork = ConstantValues(typeof(ClassroomSheddableWork));

        Assert.Contains(ClassroomDevicePerformanceProfiles.Legacy, clientProfiles);
        Assert.Contains(ClassroomDevicePerformanceProfiles.Standard, clientProfiles);
        Assert.Contains(ClassroomMasterPerformanceProfiles.MasterBalanced, masterProfiles);
        Assert.Contains(ClassroomResourceWorkClasses.ControlCritical, workClasses);
        Assert.Contains(ClassroomResourceWorkClasses.Visual, workClasses);
        Assert.Contains(ClassroomResourceWorkClasses.Background, workClasses);
        Assert.Contains(ClassroomResourcePressureStates.Degraded, pressureStates);
        Assert.Contains(ClassroomSheddableWork.Thumbnails, sheddableWork);
        Assert.Contains(ClassroomSheddableWork.NonUrgentTransfer, sheddableWork);
        Assert.DoesNotContain("AUTHORIZED", clientProfiles);
        Assert.DoesNotContain("MASTER", clientProfiles);
    }

    [Fact]
    public void ManagedWindowsAccountErrors_AreStructuredOperationCodes()
    {
        var errorCodes = ConstantValues(typeof(ClassroomOperationErrorCodes));

        Assert.Contains(ClassroomOperationErrorCodes.AccountNotConfigured, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.AccountNotFound, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.ManagedCredentialNotConfigured, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.ManagedCredentialStoreInvalid, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.ManagedCredentialProtectionFailed, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.WindowsSessionUnknown, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.WindowsSessionChanged, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.WindowsLogonFailed, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.WindowsLogoffFailed, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.SessionSwitchFailed, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.CredentialProviderUnavailable, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.OperationNotImplemented, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.PowerControlUnavailable, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.PowerControlFailed, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.BrowserPolicyNotNativeEnforceable, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.BrowserDownloadPolicyInvalid, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.BrowserDownloadPolicyExternalConflict, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.BrowserDownloadPolicyApplyFailed, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.BrowserDownloadPolicyRollbackFailed, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.BrowserDownloadPolicyRecoveryRequired, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.UrlBlockedByPolicy, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.ApplicationBindingsInvalid, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.ApplicationBindingNotFound, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.ApplicationBindingInvalid, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.ApplicationDisabled, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.ApplicationExecutableNotFound, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.ApplicationLaunchFailed, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.InputLockFailed, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.InputUnlockFailed, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.WindowsSessionUnknown, errorCodes);
    }

    [Fact]
    public void ManagedWindowsAccountBindingModel_DoesNotExposeSecretsOrCredentialIds()
    {
        var propertyNames = typeof(ManagedWindowsAccountBinding)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("AccountId", propertyNames);
        Assert.Contains("WindowsSid", propertyNames);
        Assert.Contains("AccountReference", propertyNames);
        Assert.DoesNotContain("Password", propertyNames);
        Assert.DoesNotContain("PasswordHash", propertyNames);
        Assert.DoesNotContain("Credential", propertyNames);
        Assert.DoesNotContain("CredentialId", propertyNames);
        Assert.DoesNotContain("Token", propertyNames);
        Assert.DoesNotContain("SessionId", propertyNames);
        Assert.DoesNotContain("ProfilePath", propertyNames);
    }

    [Fact]
    public void BrowserDownloadPolicyContracts_ExposeGaltekModesAndReservedCapabilityName()
    {
        var modes = ConstantValues(typeof(ClassroomBrowserDownloadRestrictionModes));
        var capabilities = ConstantValues(typeof(ClassroomCapabilities));

        Assert.Contains(ClassroomBrowserDownloadRestrictionModes.NoSpecialRestrictions, modes);
        Assert.Contains(ClassroomBrowserDownloadRestrictionModes.BlockDangerous, modes);
        Assert.Contains(ClassroomBrowserDownloadRestrictionModes.BlockPotentiallyDangerous, modes);
        Assert.Contains(ClassroomBrowserDownloadRestrictionModes.BlockAll, modes);
        Assert.Contains(ClassroomBrowserDownloadRestrictionModes.BlockMalicious, modes);
        Assert.Contains(ClassroomCapabilities.BrowserDownloadPolicyV1, capabilities);
        Assert.DoesNotContain("0", modes);
        Assert.DoesNotContain("DownloadRestrictions", modes);
    }

    [Fact]
    public void OpenApplicationRemoteContract_IsTypedApplicationIdOnly()
    {
        var request = new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.OpenApplication,
            TargetDeviceId = "device-1",
            ProtocolVersion = "1",
            OpenApplication = new OpenApplicationOperationParameters
            {
                ApplicationId = "conejito-lector"
            }
        };

        Assert.Equal(NetworkOperationType.OpenApplication, request.OperationType);
        Assert.Equal(OperationRequest.OperationParametersOneofCase.OpenApplication, request.OperationParametersCase);
        Assert.Equal("conejito-lector", request.OpenApplication.ApplicationId);

        var parameterNames = typeof(OpenApplicationOperationParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ApplicationId", parameterNames);
        Assert.DoesNotContain("ExecutablePath", parameterNames);
        Assert.DoesNotContain("Arguments", parameterNames);
        Assert.DoesNotContain("CommandLine", parameterNames);
        Assert.DoesNotContain("WorkingDirectory", parameterNames);
    }

    [Fact]
    public void Capabilities_ExposeOpenApplicationV1()
    {
        Assert.Equal(
            "OPEN_APPLICATION_V1",
            ClassroomCapabilities.OpenApplicationV1);
        Assert.True(Enum.IsDefined(NetworkCapability.OpenApplicationV1));
    }

    [Fact]
    public void InputControlRemoteContract_IsTypedOperationWithoutPayload()
    {
        var lockRequest = new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.LockInput,
            TargetDeviceId = "device-1",
            ProtocolVersion = "1"
        };
        var unlockRequest = lockRequest.Clone();
        unlockRequest.OperationId = Guid.NewGuid().ToString("D");
        unlockRequest.OperationType = NetworkOperationType.UnlockInput;

        Assert.Equal(NetworkOperationType.LockInput, lockRequest.OperationType);
        Assert.Equal(OperationRequest.OperationParametersOneofCase.None, lockRequest.OperationParametersCase);
        Assert.Equal(NetworkOperationType.UnlockInput, unlockRequest.OperationType);
        Assert.Equal(OperationRequest.OperationParametersOneofCase.None, unlockRequest.OperationParametersCase);

        var requestProperties = typeof(OperationRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Command", requestProperties);
        Assert.DoesNotContain("Arguments", requestProperties);
        Assert.DoesNotContain("Payload", requestProperties);
        Assert.DoesNotContain("Duration", requestProperties);
        Assert.DoesNotContain("KeyCodes", requestProperties);
    }

    [Fact]
    public void Capabilities_ExposeInputControlV1()
    {
        Assert.Equal(
            "INPUT_CONTROL_V1",
            ClassroomCapabilities.InputControlV1);
        Assert.True(Enum.IsDefined(NetworkCapability.InputControlV1));
    }

    [Fact]
    public void WindowsSessionStateRemoteContract_IsTypedAndDoesNotExposeIdentity()
    {
        var request = new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.GetWindowsSessionState,
            TargetDeviceId = "device-1",
            ProtocolVersion = "1"
        };
        var result = new OperationResult
        {
            OperationId = request.OperationId,
            OperationType = NetworkOperationType.GetWindowsSessionState,
            TargetDeviceId = request.TargetDeviceId,
            ProtocolVersion = "1",
            Status = OperationExecutionStatus.Success,
            WindowsSessionState = new WindowsSessionStateResult
            {
                State = WindowsSessionState.PrimaryActive
            }
        };

        Assert.Equal(OperationRequest.OperationParametersOneofCase.None, request.OperationParametersCase);
        Assert.Equal(OperationResult.ResultDetailsOneofCase.WindowsSessionState, result.ResultDetailsCase);
        Assert.Equal(WindowsSessionState.PrimaryActive, result.WindowsSessionState.State);

        var resultProperties = typeof(WindowsSessionStateResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("State", resultProperties);
        Assert.DoesNotContain("WindowsSid", resultProperties);
        Assert.DoesNotContain("Username", resultProperties);
        Assert.DoesNotContain("SessionId", resultProperties);
        Assert.DoesNotContain("AccountReference", resultProperties);
        Assert.NotEqual(WindowsSessionState.Unspecified, result.WindowsSessionState.State);
    }

    [Fact]
    public void Capabilities_ExposeWindowsSessionStateV1()
    {
        Assert.Equal(
            "WINDOWS_SESSION_STATE_V1",
            ClassroomCapabilities.WindowsSessionStateV1);
        Assert.True(Enum.IsDefined(NetworkCapability.WindowsSessionStateV1));
    }

    [Fact]
    public void LogoffWindowsSessionRemoteContract_IsExpectedAccountIdOnly()
    {
        var request = new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.LogoffWindowsSession,
            TargetDeviceId = "device-1",
            ProtocolVersion = "1",
            LogoffWindowsSession = new LogoffWindowsSessionOperationParameters
            {
                AccountId = ManagedWindowsAccountId.Primary
            }
        };

        Assert.Equal(NetworkOperationType.LogoffWindowsSession, request.OperationType);
        Assert.Equal(
            OperationRequest.OperationParametersOneofCase.LogoffWindowsSession,
            request.OperationParametersCase);
        Assert.Equal(ManagedWindowsAccountId.Primary, request.LogoffWindowsSession.AccountId);

        var parameterNames = typeof(LogoffWindowsSessionOperationParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("AccountId", parameterNames);
        Assert.DoesNotContain("Username", parameterNames);
        Assert.DoesNotContain("Domain", parameterNames);
        Assert.DoesNotContain("WindowsSid", parameterNames);
        Assert.DoesNotContain("Sid", parameterNames);
        Assert.DoesNotContain("SessionId", parameterNames);
        Assert.DoesNotContain("Password", parameterNames);
        Assert.DoesNotContain("Force", parameterNames);
        Assert.DoesNotContain("Timeout", parameterNames);
        Assert.DoesNotContain("Command", parameterNames);
        Assert.DoesNotContain("Arguments", parameterNames);
        Assert.DoesNotContain("Shell", parameterNames);
    }

    [Fact]
    public void Capabilities_ExposeWindowsSessionLogoffV1()
    {
        Assert.Equal(
            "WINDOWS_SESSION_LOGOFF_V1",
            ClassroomCapabilities.WindowsSessionLogoffV1);
        Assert.True(Enum.IsDefined(NetworkCapability.WindowsSessionLogoffV1));
    }

    [Fact]
    public void ProvisionManagedCredentialRemoteContract_IsTypedSecretBearingBytesOnly()
    {
        var request = new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.ProvisionManagedCredential,
            TargetDeviceId = "device-1",
            ProtocolVersion = "1",
            ProvisionManagedCredential = new ProvisionManagedCredentialOperationParameters
            {
                AccountId = ManagedWindowsAccountId.Primary,
                PasswordUtf16Le = Google.Protobuf.ByteString.CopyFrom([0x41, 0x00])
            }
        };

        Assert.Equal(NetworkOperationType.ProvisionManagedCredential, request.OperationType);
        Assert.Equal(
            OperationRequest.OperationParametersOneofCase.ProvisionManagedCredential,
            request.OperationParametersCase);
        Assert.Equal(ManagedWindowsAccountId.Primary, request.ProvisionManagedCredential.AccountId);
        Assert.Equal([0x41, 0x00], request.ProvisionManagedCredential.PasswordUtf16Le.ToByteArray());

        var parameterNames = typeof(ProvisionManagedCredentialOperationParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("AccountId", parameterNames);
        Assert.Contains("PasswordUtf16Le", parameterNames);
        Assert.DoesNotContain("Password", parameterNames);
        Assert.DoesNotContain("Username", parameterNames);
        Assert.DoesNotContain("WindowsSid", parameterNames);
        Assert.DoesNotContain("Sid", parameterNames);
        Assert.DoesNotContain("CredentialId", parameterNames);
        Assert.DoesNotContain("VaultSessionToken", parameterNames);
        Assert.DoesNotContain("MasterPassword", parameterNames);
        Assert.DoesNotContain("ProfilePath", parameterNames);
        Assert.DoesNotContain("Command", parameterNames);
        Assert.DoesNotContain("Arguments", parameterNames);
        Assert.DoesNotContain("Shell", parameterNames);
    }

    [Fact]
    public void Capabilities_ExposeManagedCredentialProvisioningV1()
    {
        Assert.Equal(
            "MANAGED_CREDENTIAL_PROVISIONING_V1",
            ClassroomCapabilities.ManagedCredentialProvisioningV1);
        Assert.True(Enum.IsDefined(NetworkCapability.ManagedCredentialProvisioningV1));
    }

    private static HashSet<string> ConstantValues(Type type)
    {
        return type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
