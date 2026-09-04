using GaltekClassroom.Agent.Service.Master;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class WindowsAccountResolverTests
{
    [Theory]
    [InlineData("Primaria", "PC23", "PC23\\Primaria")]
    [InlineData("  Primaria  ", "PC23", "PC23\\Primaria")]
    [InlineData("AULA\\Primaria", "PC23", "AULA\\Primaria")]
    [InlineData("primaria@example.local", "PC23", "primaria@example.local")]
    public void NormalizeAccountName_QualifiesShortNamesAsLocalMachineAccounts(
        string accountName,
        string machineName,
        string expected)
    {
        Assert.Equal(expected, WindowsAccountNameNormalizer.NormalizeAccountName(accountName, machineName));
    }
}
