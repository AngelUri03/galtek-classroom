using System.Reflection;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class OperationContractsTests
{
    [Fact]
    public void OperationTypes_ExposeTypedClassroomActions()
    {
        var operations = ConstantValues(typeof(ClassroomOperationTypes));

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
        var sessionStates = ConstantValues(typeof(ClassroomWindowsSessionStates));
        var switchActions = ConstantValues(typeof(ClassroomManagedAccountSwitchActions));

        Assert.Contains(ClassroomManagedWindowsAccountTypes.Primary, accountTypes);
        Assert.Contains(ClassroomManagedWindowsAccountTypes.Secondary, accountTypes);
        Assert.DoesNotContain("Administrator", accountTypes);
        Assert.Contains(ClassroomWindowsSessionStates.NoSession, sessionStates);
        Assert.Contains(ClassroomWindowsSessionStates.PrimaryActive, sessionStates);
        Assert.Contains(ClassroomWindowsSessionStates.SecondaryActive, sessionStates);
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
        Assert.Contains(ClassroomOperationErrorCodes.ManagedCredentialNotConfigured, errorCodes);
        Assert.Contains(ClassroomOperationErrorCodes.WindowsSessionUnknown, errorCodes);
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

    private static HashSet<string> ConstantValues(Type type)
    {
        return type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
