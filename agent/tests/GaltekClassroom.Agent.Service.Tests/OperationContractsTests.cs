using System.Reflection;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class OperationContractsTests
{
    [Fact]
    public void OperationTypes_ExposeTypedClassroomActions()
    {
        var operations = ConstantValues(typeof(ClassroomOperationTypes));

        Assert.Contains(ClassroomOperationTypes.OpenApplication, operations);
        Assert.Contains(ClassroomOperationTypes.OpenUrl, operations);
        Assert.Contains(ClassroomOperationTypes.DistributeFile, operations);
        Assert.Contains(ClassroomOperationTypes.MoveStudent, operations);
        Assert.Contains(ClassroomOperationTypes.SwapStudents, operations);
        Assert.Contains(ClassroomOperationTypes.GetWindowsSessionState, operations);
        Assert.Contains(ClassroomOperationTypes.LogonManagedAccount, operations);
        Assert.Contains(ClassroomOperationTypes.LogoffWindowsSession, operations);
        Assert.Contains(ClassroomOperationTypes.SwitchManagedAccount, operations);
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
        Assert.DoesNotContain(@"C:\Windows\System32", destinations);
        Assert.DoesNotContain("..\\..\\Windows", destinations);
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
    }

    private static HashSet<string> ConstantValues(Type type)
    {
        return type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
