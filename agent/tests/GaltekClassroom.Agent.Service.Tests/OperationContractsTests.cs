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

    private static HashSet<string> ConstantValues(Type type)
    {
        return type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
