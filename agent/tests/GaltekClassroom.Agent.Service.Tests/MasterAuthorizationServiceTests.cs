using GaltekClassroom.Agent.Service.Ipc;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class MasterAuthorizationServiceTests
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
    private const string BoundSid = "S-1-5-21-1000000000-1000000000-1000000000-1001";
    private const string OtherSid = "S-1-5-21-1000000000-1000000000-1000000000-1002";

    [Fact]
    public void Evaluate_WhenLicenseBindingInstallationAndSidMatch_Authorizes()
    {
        var result = Evaluate(
            ActiveLicense(CommercialLicenseConstants.ClientRole, CommercialLicenseConstants.MasterRole),
            LoadedBinding(Binding(InstallationId, BoundSid)),
            new LocalIpcClientContext(BoundSid, "AULA\\MaestraPrimaria"));

        Assert.True(result.Authorized);
        Assert.True(result.Configured);
        Assert.Equal("AUTHORIZED", result.Status);
        Assert.Equal("AULA\\MaestraPrimaria", result.BoundAccountDisplayName);
        Assert.Equal("AULA\\MaestraPrimaria", result.CurrentAccountDisplayName);
    }

    [Fact]
    public void Evaluate_WhenSidDiffers_DoesNotAllowAdministratorBypass()
    {
        var result = Evaluate(
            ActiveLicense(CommercialLicenseConstants.ClientRole, CommercialLicenseConstants.MasterRole),
            LoadedBinding(Binding(InstallationId, BoundSid)),
            new LocalIpcClientContext(OtherSid, "AULA\\SoporteAdministrador"));

        Assert.False(result.Authorized);
        Assert.Equal("CURRENT_ACCOUNT_NOT_AUTHORIZED", result.Status);
    }

    [Fact]
    public void Evaluate_WhenLicenseIsClientOnly_ReturnsMasterLicenseRequired()
    {
        var result = Evaluate(
            ActiveLicense(CommercialLicenseConstants.ClientRole),
            LoadedBinding(Binding(InstallationId, BoundSid)),
            new LocalIpcClientContext(BoundSid, "AULA\\MaestraPrimaria"));

        Assert.False(result.Authorized);
        Assert.Equal("MASTER_LICENSE_REQUIRED", result.Status);
    }

    [Fact]
    public void Evaluate_WhenLicenseIsExpired_ReturnsMasterLicenseRequired()
    {
        var result = Evaluate(
            LicenseState.Blocked(
                CommercialLicenseStatus.LicenseExpired,
                FixedNowUtc,
                "expired"),
            LoadedBinding(Binding(InstallationId, BoundSid)),
            new LocalIpcClientContext(BoundSid, "AULA\\MaestraPrimaria"));

        Assert.False(result.Authorized);
        Assert.Equal("MASTER_LICENSE_REQUIRED", result.Status);
    }

    [Fact]
    public void Evaluate_WhenBindingIsMissing_ReturnsNotConfigured()
    {
        var result = Evaluate(
            ActiveLicense(CommercialLicenseConstants.ClientRole, CommercialLicenseConstants.MasterRole),
            MasterBindingStoreReadResult.Missing("master-binding.json"),
            new LocalIpcClientContext(BoundSid, "AULA\\MaestraPrimaria"));

        Assert.False(result.Authorized);
        Assert.False(result.Configured);
        Assert.Equal("NOT_CONFIGURED", result.Status);
    }

    [Fact]
    public void Evaluate_WhenBindingIsInvalid_ReturnsMasterBindingInvalid()
    {
        var result = Evaluate(
            ActiveLicense(CommercialLicenseConstants.ClientRole, CommercialLicenseConstants.MasterRole),
            MasterBindingStoreReadResult.Invalid("master-binding.json", "corrupt"),
            new LocalIpcClientContext(BoundSid, "AULA\\MaestraPrimaria"));

        Assert.False(result.Authorized);
        Assert.True(result.Configured);
        Assert.Equal("MASTER_BINDING_INVALID", result.Status);
    }

    [Fact]
    public void Evaluate_WhenBindingInstallationDiffers_ReturnsInstallationMismatch()
    {
        var result = Evaluate(
            ActiveLicense(CommercialLicenseConstants.ClientRole, CommercialLicenseConstants.MasterRole),
            LoadedBinding(Binding(Guid.Parse("bbbbbbbb-bbbb-cccc-dddd-eeeeeeeeeeee"), BoundSid)),
            new LocalIpcClientContext(BoundSid, "AULA\\MaestraPrimaria"));

        Assert.False(result.Authorized);
        Assert.Equal("MASTER_BINDING_INSTALLATION_MISMATCH", result.Status);
    }

    [Fact]
    public void Evaluate_WhenCallerSidIsUnavailable_FailsClosed()
    {
        var result = Evaluate(
            ActiveLicense(CommercialLicenseConstants.ClientRole, CommercialLicenseConstants.MasterRole),
            LoadedBinding(Binding(InstallationId, BoundSid)),
            LocalIpcClientContext.Unavailable());

        Assert.False(result.Authorized);
        Assert.Equal("CURRENT_ACCOUNT_NOT_AUTHORIZED", result.Status);
    }

    private static LocalMasterAuthorization Evaluate(
        LicenseState licenseState,
        MasterBindingStoreReadResult bindingResult,
        LocalIpcClientContext clientContext)
    {
        return MasterAuthorizationService.Evaluate(
            InstallationIdentity.Create(
                InstallationId,
                new HardwareFingerprint("a", "b", "c", "d"),
                FixedNowUtc),
            licenseState,
            bindingResult,
            clientContext);
    }

    private static MasterBindingStoreReadResult LoadedBinding(MasterWindowsBinding binding)
    {
        return MasterBindingStoreReadResult.Loaded(binding, "master-binding.json");
    }

    private static MasterWindowsBinding Binding(Guid installationId, string sid)
    {
        return MasterWindowsBinding.Create(
            installationId,
            sid,
            "AULA\\MaestraPrimaria",
            FixedNowUtc);
    }

    private static LicenseState ActiveLicense(params string[] roles)
    {
        return LicenseState.ActiveState(
            "license-1",
            "ORG-1",
            roles,
            CommercialLicenseFeatures.Empty,
            FixedNowUtc.AddDays(30),
            FixedNowUtc);
    }
}
