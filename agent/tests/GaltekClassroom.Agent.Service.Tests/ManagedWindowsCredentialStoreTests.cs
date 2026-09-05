using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class ManagedWindowsCredentialStoreTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherInstallationId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private const string PrimarySid = "S-1-5-21-1000000000-1000000000-1000000000-1004";
    private const string SecondarySid = "S-1-5-21-1000000000-1000000000-1000000000-1005";
    private const string NewPrimarySid = "S-1-5-21-1000000000-1000000000-1000000000-2004";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.ManagedWindowsCredentials.Tests",
        Guid.NewGuid().ToString("N"));

    private readonly FakeCredentialProtector _protector = new();
    private readonly FakeWindowsAccountResolver _resolver = CreateDefaultResolver();

    [Fact]
    public async Task GetStatusAsync_WhenStoreIsMissing_ReturnsCredentialNotConfiguredAndDoesNotCreateFile()
    {
        await SaveBindingsAsync([PrimaryBinding()]);

        var result = await CreateStore().GetStatusAsync(
            InstallationId,
            ClassroomManagedWindowsAccountTypes.Primary,
            CancellationToken.None);

        Assert.Equal(ManagedWindowsCredentialStatus.CredentialNotConfigured, result.Status);
        Assert.False(result.CredentialConfigured);
        Assert.False(File.Exists(CredentialFilePath()));
    }

    [Fact]
    public async Task AddAsync_WhenPrimaryBindingExists_CreatesEncryptedStore()
    {
        await SaveBindingsAsync([PrimaryBinding()]);

        var result = await CreateStore().AddAsync(
            InstallationId,
            ClassroomManagedWindowsAccountTypes.Primary,
            Chars("Clase 1!"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(CredentialFilePath()));
        Assert.DoesNotContain("Clase 1!", await File.ReadAllTextAsync(CredentialFilePath()));
    }

    [Fact]
    public async Task AddAsync_WhenSecondaryBindingExists_AddsSecondary()
    {
        await SaveBindingsAsync([PrimaryBinding(), SecondaryBinding()]);
        var store = CreateStore();

        await store.AddAsync(InstallationId, ClassroomManagedWindowsAccountTypes.Primary, Chars("primary"), CancellationToken.None);
        var result = await store.AddAsync(InstallationId, ClassroomManagedWindowsAccountTypes.Secondary, Chars("secondary"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("SECONDARY", await File.ReadAllTextAsync(CredentialFilePath()));
    }

    [Fact]
    public async Task AcquireAsync_AfterReload_ReturnsLeaseAndDoesNotExposeString()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        await CreateStore().AddAsync(InstallationId, ClassroomManagedWindowsAccountTypes.Primary, Chars("secret-value"), CancellationToken.None);

        var result = await CreateStore().AcquireAsync(
            InstallationId,
            ClassroomManagedWindowsAccountTypes.Primary,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        using var lease = result.Lease!;
        Assert.Equal("secret-value", Encoding.Unicode.GetString(lease.PasswordUtf16LittleEndian.Span));
        Assert.DoesNotContain("secret-value", lease.ToString());
    }

    [Fact]
    public async Task LoadValidation_WhenPrimaryIsDuplicated_FailsClosed()
    {
        await WriteRawCredentialDocumentAsync(new
        {
            schemaVersion = ManagedWindowsCredentialConstants.SchemaVersion,
            installationId = InstallationId,
            entries = new object[]
            {
                Entry("PRIMARY", [1, 2, 3]),
                Entry("PRIMARY", [4, 5, 6])
            }
        });

        var result = await CreateStore().GetStatusAsync(
            InstallationId,
            ClassroomManagedWindowsAccountTypes.Primary,
            CancellationToken.None);

        Assert.Equal(ManagedWindowsCredentialStatus.StoreInvalid, result.Status);
        Assert.Equal(ManagedWindowsCredentialErrorCodes.ManagedCredentialStoreInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task ReplaceAsync_WhenPrimaryExists_ExplicitlyOverwritesCredential()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var store = CreateStore();
        await store.AddAsync(InstallationId, "PRIMARY", Chars("old"), CancellationToken.None);

        var result = await store.ReplaceAsync(InstallationId, "PRIMARY", Chars("new"), CancellationToken.None);
        var acquired = await store.AcquireAsync(InstallationId, "PRIMARY", CancellationToken.None);

        Assert.True(result.Succeeded);
        using var lease = acquired.Lease!;
        Assert.Equal("new", Encoding.Unicode.GetString(lease.PasswordUtf16LittleEndian.Span));
    }

    [Fact]
    public async Task ReplaceAsync_WhenPrimaryExists_PreservesSecondary()
    {
        await SaveBindingsAsync([PrimaryBinding(), SecondaryBinding()]);
        var store = CreateStore();
        await store.AddAsync(InstallationId, "PRIMARY", Chars("old-primary"), CancellationToken.None);
        await store.AddAsync(InstallationId, "SECONDARY", Chars("secondary"), CancellationToken.None);

        await store.ReplaceAsync(InstallationId, "PRIMARY", Chars("new-primary"), CancellationToken.None);
        var secondary = await store.AcquireAsync(InstallationId, "SECONDARY", CancellationToken.None);

        using var lease = secondary.Lease!;
        Assert.Equal("secondary", Encoding.Unicode.GetString(lease.PasswordUtf16LittleEndian.Span));
    }

    [Fact]
    public async Task RemoveAsync_WhenPrimaryExists_PreservesSecondary()
    {
        await SaveBindingsAsync([PrimaryBinding(), SecondaryBinding()]);
        var store = CreateStore();
        await store.AddAsync(InstallationId, "PRIMARY", Chars("primary"), CancellationToken.None);
        await store.AddAsync(InstallationId, "SECONDARY", Chars("secondary"), CancellationToken.None);

        var remove = await store.RemoveAsync(InstallationId, "PRIMARY", CancellationToken.None);
        var secondary = await store.GetStatusAsync(InstallationId, "SECONDARY", CancellationToken.None);
        var primary = await store.GetStatusAsync(InstallationId, "PRIMARY", CancellationToken.None);

        Assert.True(remove.Succeeded);
        Assert.True(secondary.CredentialConfigured);
        Assert.False(primary.CredentialConfigured);
    }

    [Theory]
    [InlineData(99, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "PRIMARY", "AQID")]
    [InlineData(1, "bbbbbbbb-cccc-dddd-eeee-ffffffffffff", "PRIMARY", "AQID")]
    [InlineData(1, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "TERTIARY", "AQID")]
    [InlineData(1, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "PRIMARY", "not-base64")]
    public async Task LoadValidation_WhenEnvelopeIsInvalid_FailsClosed(
        int schemaVersion,
        string installationId,
        string accountId,
        string protectedData)
    {
        await WriteRawCredentialDocumentAsync(new
        {
            schemaVersion,
            installationId,
            entries = new object[]
            {
                new
                {
                    accountId,
                    protectedData,
                    createdAtUtc = FixedNow,
                    updatedAtUtc = FixedNow
                }
            }
        });

        var result = await CreateStore().GetStatusAsync(
            InstallationId,
            ClassroomManagedWindowsAccountTypes.Primary,
            CancellationToken.None);

        Assert.Equal(ManagedWindowsCredentialStatus.StoreInvalid, result.Status);
    }

    [Fact]
    public async Task LoadValidation_WhenJsonIsCorrupt_ReturnsInvalidAndPreservesFile()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(CredentialFilePath(), "{ corrupt");

        var result = await CreateStore().GetStatusAsync(InstallationId, "PRIMARY", CancellationToken.None);

        Assert.Equal(ManagedWindowsCredentialStatus.StoreInvalid, result.Status);
        Assert.Equal("{ corrupt", await File.ReadAllTextAsync(CredentialFilePath()));
    }

    [Fact]
    public async Task PersistedEnvelope_DoesNotContainPlaintextSecretOrBindingIdentity()
    {
        await SaveBindingsAsync([PrimaryBinding()]);

        await CreateStore().AddAsync(InstallationId, "PRIMARY", Chars("P@ssw0rd with spaces"), CancellationToken.None);
        var json = await File.ReadAllTextAsync(CredentialFilePath());

        Assert.DoesNotContain("P@ssw0rd", json, StringComparison.Ordinal);
        Assert.DoesNotContain(PrimarySid, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PC23", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Primaria", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credentialId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddAsync_WhenBindingIsMissing_ReturnsAccountNotConfigured()
    {
        await SaveBindingsAsync([]);

        var result = await CreateStore().AddAsync(InstallationId, "PRIMARY", Chars("secret"), CancellationToken.None);

        Assert.Equal(ManagedWindowsCredentialWriteStatus.AccountNotConfigured, result.Status);
        Assert.Equal(ManagedWindowsCredentialErrorCodes.AccountNotConfigured, result.ErrorCode);
    }

    [Fact]
    public async Task AddAsync_WhenBindingSidNoLongerResolves_ReturnsAccountNotFound()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        _resolver.Sids.Remove(PrimarySid);

        var result = await CreateStore().AddAsync(InstallationId, "PRIMARY", Chars("secret"), CancellationToken.None);

        Assert.Equal(ManagedWindowsCredentialWriteStatus.AccountNotFound, result.Status);
        Assert.Equal(ManagedWindowsCredentialErrorCodes.AccountNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task RenameWithSameSid_KeepsCredentialUsable()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        await CreateStore().AddAsync(InstallationId, "PRIMARY", Chars("secret"), CancellationToken.None);
        _resolver.Sids[PrimarySid] = User(PrimarySid, "PC23\\Primaria2026");

        var status = await CreateStore().GetStatusAsync(InstallationId, "PRIMARY", CancellationToken.None);

        Assert.True(status.CredentialConfigured);
        Assert.Equal(ManagedWindowsCredentialStatus.Usable, status.Status);
    }

    [Fact]
    public async Task RebindToDifferentSid_MakesOldCredentialLogicallyUnusable()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var store = CreateStore();
        await store.AddAsync(InstallationId, "PRIMARY", Chars("old"), CancellationToken.None);
        await ReplacePrimaryBindingAsync(NewPrimarySid, "PC23\\PrimariaNueva");

        var status = await store.GetStatusAsync(InstallationId, "PRIMARY", CancellationToken.None);
        var acquire = await store.AcquireAsync(InstallationId, "PRIMARY", CancellationToken.None);

        Assert.False(status.CredentialConfigured);
        Assert.Equal(ManagedWindowsCredentialStatus.CredentialNotConfigured, status.Status);
        Assert.False(acquire.Succeeded);
        Assert.Null(acquire.Lease);
    }

    [Fact]
    public async Task ReplaceCredentialAfterRebind_MakesNewSidUsable()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var store = CreateStore();
        await store.AddAsync(InstallationId, "PRIMARY", Chars("old"), CancellationToken.None);
        await ReplacePrimaryBindingAsync(NewPrimarySid, "PC23\\PrimariaNueva");

        await store.ReplaceAsync(InstallationId, "PRIMARY", Chars("new"), CancellationToken.None);
        var acquire = await store.AcquireAsync(InstallationId, "PRIMARY", CancellationToken.None);

        using var lease = acquire.Lease!;
        Assert.Equal(NewPrimarySid, lease.WindowsSid);
        Assert.Equal("new", Encoding.Unicode.GetString(lease.PasswordUtf16LittleEndian.Span));
    }

    [Fact]
    public async Task RecreatedSameAccountReferenceWithDifferentSid_DoesNotAdoptOldCredential()
    {
        await SaveBindingsAsync([PrimaryBinding(accountReference: "PC23\\Primaria")]);
        var store = CreateStore();
        await store.AddAsync(InstallationId, "PRIMARY", Chars("old"), CancellationToken.None);
        await ReplacePrimaryBindingAsync(NewPrimarySid, "PC23\\Primaria");

        var status = await store.GetStatusAsync(InstallationId, "PRIMARY", CancellationToken.None);

        Assert.False(status.CredentialConfigured);
    }

    [Fact]
    public async Task ProtectReceivesEntropyDerivedFromInstallationAndAccount()
    {
        await SaveBindingsAsync([PrimaryBinding()]);

        await CreateStore().AddAsync(InstallationId, "PRIMARY", Chars("secret"), CancellationToken.None);

        var expected = Encoding.UTF8.GetString(
            ManagedWindowsCredentialStore.DeriveOptionalEntropy(InstallationId, "PRIMARY"));
        Assert.Equal(expected, Encoding.UTF8.GetString(_protector.LastProtectEntropy!));
    }

    [Fact]
    public void EntropyDiffersBySlotAndInstallation()
    {
        Assert.NotEqual(
            ManagedWindowsCredentialStore.DeriveOptionalEntropy(InstallationId, "PRIMARY"),
            ManagedWindowsCredentialStore.DeriveOptionalEntropy(InstallationId, "SECONDARY"));
        Assert.NotEqual(
            ManagedWindowsCredentialStore.DeriveOptionalEntropy(InstallationId, "PRIMARY"),
            ManagedWindowsCredentialStore.DeriveOptionalEntropy(OtherInstallationId, "PRIMARY"));
    }

    [Fact]
    public async Task ProtectorFailure_ReturnsStructuredError()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        _protector.FailProtect = true;

        var result = await CreateStore().AddAsync(InstallationId, "PRIMARY", Chars("secret"), CancellationToken.None);

        Assert.Equal(ManagedWindowsCredentialWriteStatus.ProtectionFailed, result.Status);
        Assert.Equal(ManagedWindowsCredentialErrorCodes.ManagedCredentialProtectionFailed, result.ErrorCode);
    }

    [Fact]
    public async Task TamperedProtectedBlob_FailsClosed()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        await CreateStore().AddAsync(InstallationId, "PRIMARY", Chars("secret"), CancellationToken.None);
        var json = await File.ReadAllTextAsync(CredentialFilePath());
        var document = JsonSerializer.Deserialize<ManagedWindowsCredentialCatalogDocument>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await WriteRawCredentialDocumentAsync(new
        {
            schemaVersion = ManagedWindowsCredentialConstants.SchemaVersion,
            installationId = InstallationId,
            entries = new object[]
            {
                Entry(document.Entries[0].AccountId, [9, 9, 9])
            }
        });

        var acquire = await CreateStore().AcquireAsync(InstallationId, "PRIMARY", CancellationToken.None);

        Assert.False(acquire.Succeeded);
        Assert.Equal(ManagedWindowsCredentialAcquireStatus.ProtectionFailed, acquire.Status);
    }

    [Fact]
    public async Task PlaintextPayloadIsZeroedAfterProtectSuccessAndFailure()
    {
        await SaveBindingsAsync([PrimaryBinding()]);

        await CreateStore().AddAsync(InstallationId, "PRIMARY", Chars("secret"), CancellationToken.None);
        var successPayload = _protector.LastProtectPlaintext!;

        _protector.FailProtect = true;
        await CreateStore().ReplaceAsync(InstallationId, "PRIMARY", Chars("other"), CancellationToken.None);
        var failurePayload = _protector.LastProtectPlaintext!;

        Assert.All(successPayload, value => Assert.Equal(0, value));
        Assert.All(failurePayload, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task DecryptedPayloadIsZeroedAfterProducingLease()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        await CreateStore().AddAsync(InstallationId, "PRIMARY", Chars("secret"), CancellationToken.None);

        var acquire = await CreateStore().AcquireAsync(InstallationId, "PRIMARY", CancellationToken.None);

        Assert.True(acquire.Succeeded);
        Assert.All(_protector.LastUnprotectedPlaintext!, value => Assert.Equal(0, value));
        acquire.Lease!.Dispose();
    }

    [Fact]
    public async Task LeaseDisposeZeroesSecretBuffer()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        await CreateStore().AddAsync(InstallationId, "PRIMARY", Chars("secret"), CancellationToken.None);
        var acquire = await CreateStore().AcquireAsync(InstallationId, "PRIMARY", CancellationToken.None);
        var lease = acquire.Lease!;
        var before = lease.PasswordUtf16LittleEndian.ToArray();

        lease.Dispose();

        Assert.Contains(before, value => value != 0);
        Assert.Throws<ObjectDisposedException>(() => _ = lease.PasswordUtf16LittleEndian);
    }

    [Fact]
    public async Task AcquireFailureDoesNotReturnLease()
    {
        await SaveBindingsAsync([PrimaryBinding()]);

        var acquire = await CreateStore().AcquireAsync(InstallationId, "PRIMARY", CancellationToken.None);

        Assert.False(acquire.Succeeded);
        Assert.Null(acquire.Lease);
    }

    [Fact]
    public async Task StoreAppliesFileSecurityToTemporaryAndTargetFile()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var security = new RecordingCredentialFileSecurity();

        await CreateStore(fileSecurity: security).AddAsync(InstallationId, "PRIMARY", Chars("secret"), CancellationToken.None);

        Assert.Contains(security.Paths, path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(security.Paths, path => path.EndsWith(ManagedWindowsCredentialConstants.FileName, StringComparison.Ordinal));
    }

    [Fact]
    public void ProductProtector_UsesUiForbiddenWithoutLocalMachineScope()
    {
        var native = new FakeDpapiNative();
        var protector = new WindowsDpapiManagedWindowsCredentialProtector(native);

        var result = protector.Protect([1, 2, 3], [4, 5, 6]);

        Assert.True(result.Succeeded);
        Assert.Equal(WindowsDpapiManagedWindowsCredentialProtector.CryptProtectUiForbidden, native.LastProtectFlags);
        Assert.Equal(0, native.LastProtectFlags & 0x4);
        Assert.Equal(1, native.ProtectCalls);
    }

    [Fact]
    public void ProductProtector_RequiresLocalSystem()
    {
        var native = new FakeDpapiNative { RunningAsLocalSystem = false };
        var protector = new WindowsDpapiManagedWindowsCredentialProtector(native);

        var result = protector.Protect([1, 2, 3], [4, 5, 6]);

        Assert.False(result.Succeeded);
        Assert.Equal(0, native.ProtectCalls);
    }

    [Fact]
    public void ProductProtector_UnprotectTamperFailureIsStructured()
    {
        var native = new FakeDpapiNative { FailUnprotect = true };
        var protector = new WindowsDpapiManagedWindowsCredentialProtector(native);

        var result = protector.Unprotect([1, 2, 3], [4, 5, 6]);

        Assert.False(result.Succeeded);
        Assert.Equal(ManagedWindowsCredentialProtectionStatus.ProtectionFailed, result.Status);
        Assert.Equal(WindowsDpapiManagedWindowsCredentialProtector.CryptProtectUiForbidden, native.LastUnprotectFlags);
        Assert.Equal(0, native.LastUnprotectFlags & 0x4);
    }

    [Fact]
    public void PublicStoreApi_DoesNotExposeRevealDumpOrPasswordStringMethods()
    {
        var methodNames = typeof(IManagedWindowsCredentialStore)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain(methodNames, name => name.Contains("Reveal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methodNames, name => name.Contains("Dump", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methodNames, name => name.Contains("Export", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methodNames, name => name.Contains("GetPasswordString", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CredentialEnvelopeToString_DoesNotContainProtectedBlobOrSecretTerms()
    {
        var entry = new ManagedWindowsCredentialEntry("PRIMARY", "base64-protected-blob", FixedNow, FixedNow);
        var document = new ManagedWindowsCredentialCatalogDocument(
            ManagedWindowsCredentialConstants.SchemaVersion,
            InstallationId,
            [entry]);

        Assert.DoesNotContain("base64-protected-blob", entry.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("protected-blob", document.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", entry.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsCredentialFileSecurity_DoesNotGrantStandardUsersAccessInAcl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(CredentialFilePath(), "{}");

        new WindowsManagedWindowsCredentialFileSecurity().Apply(CredentialFilePath());
#pragma warning disable CA1416
        var security = FileSystemAclExtensions.GetAccessControl(new FileInfo(CredentialFilePath()));
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();

        Assert.DoesNotContain(rules, rule =>
            rule.IdentityReference == new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)
            || rule.IdentityReference == new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null));
#pragma warning restore CA1416
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private ManagedWindowsCredentialStore CreateStore(
        IManagedWindowsCredentialFileSecurity? fileSecurity = null)
    {
        return new ManagedWindowsCredentialStore(
            new ManagedWindowsCredentialStoreOptions(_dataDirectory),
            CreateBindingStore(),
            _resolver,
            _protector,
            fileSecurity ?? new NoOpManagedWindowsCredentialFileSecurity(),
            new FakeClock(FixedNow));
    }

    private ManagedWindowsAccountBindingStore CreateBindingStore()
    {
        return new ManagedWindowsAccountBindingStore(
            new ManagedWindowsAccountBindingStoreOptions(_dataDirectory),
            new NoOpManagedWindowsAccountBindingFileSecurity());
    }

    private async Task SaveBindingsAsync(IReadOnlyList<ManagedWindowsAccountBinding> bindings)
    {
        Directory.CreateDirectory(_dataDirectory);
        if (File.Exists(Path.Combine(_dataDirectory, ManagedWindowsAccountBindingConstants.FileName)))
        {
            File.Delete(Path.Combine(_dataDirectory, ManagedWindowsAccountBindingConstants.FileName));
        }

        var store = CreateBindingStore();
        foreach (var binding in bindings)
        {
            var write = await store.AddAsync(InstallationId, binding, CancellationToken.None);
            Assert.True(write.Succeeded);
        }
    }

    private async Task ReplacePrimaryBindingAsync(string windowsSid, string accountReference)
    {
        _resolver.Sids[windowsSid] = User(windowsSid, accountReference);
        var write = await CreateBindingStore().ReplaceAsync(
            InstallationId,
            PrimaryBinding(accountReference, windowsSid),
            CancellationToken.None);
        Assert.True(write.Succeeded);
    }

    private string CredentialFilePath()
    {
        return Path.Combine(_dataDirectory, ManagedWindowsCredentialConstants.FileName);
    }

    private async Task WriteRawCredentialDocumentAsync(object document)
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            CredentialFilePath(),
            JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static object Entry(string accountId, byte[] protectedData)
    {
        return new
        {
            accountId,
            protectedData = Convert.ToBase64String(protectedData),
            createdAtUtc = FixedNow,
            updatedAtUtc = FixedNow
        };
    }

    private static ReadOnlyMemory<char> Chars(string value)
    {
        return value.AsMemory();
    }

    private static ManagedWindowsAccountBinding PrimaryBinding(
        string accountReference = "PC23\\Primaria",
        string windowsSid = PrimarySid)
    {
        return ManagedWindowsAccountBinding.Create(
            ClassroomManagedWindowsAccountTypes.Primary,
            windowsSid,
            accountReference,
            FixedNow);
    }

    private static ManagedWindowsAccountBinding SecondaryBinding()
    {
        return ManagedWindowsAccountBinding.Create(
            ClassroomManagedWindowsAccountTypes.Secondary,
            SecondarySid,
            "PC23\\Secundaria",
            FixedNow);
    }

    private static FakeWindowsAccountResolver CreateDefaultResolver()
    {
        var resolver = new FakeWindowsAccountResolver();
        resolver.Sids[PrimarySid] = User(PrimarySid, "PC23\\Primaria");
        resolver.Sids[SecondarySid] = User(SecondarySid, "PC23\\Secundaria");
        resolver.Sids[NewPrimarySid] = User(NewPrimarySid, "PC23\\PrimariaNueva");
        return resolver;
    }

    private static WindowsAccountIdentity User(string sid, string accountReference)
    {
        return new WindowsAccountIdentity(sid, accountReference, WindowsAccountSidNameUse.User);
    }

    private sealed class FakeCredentialProtector : IManagedWindowsCredentialProtector
    {
        private readonly Dictionary<string, byte[]> _payloads = new(StringComparer.Ordinal);
        private int _counter;

        public bool FailProtect { get; set; }
        public byte[]? LastProtectEntropy { get; private set; }
        public byte[]? LastProtectPlaintext { get; private set; }
        public byte[]? LastUnprotectedPlaintext { get; private set; }

        public ManagedWindowsCredentialProtectionResult Protect(byte[] plaintext, byte[] optionalEntropy)
        {
            LastProtectPlaintext = plaintext;
            LastProtectEntropy = optionalEntropy.ToArray();

            if (FailProtect)
            {
                return ManagedWindowsCredentialProtectionResult.Failed("failed");
            }

            var protectedData = Encoding.UTF8.GetBytes($"protected-{++_counter}");
            _payloads[Convert.ToBase64String(protectedData)] = plaintext.ToArray();
            return ManagedWindowsCredentialProtectionResult.Protected(protectedData);
        }

        public ManagedWindowsCredentialProtectionResult Unprotect(byte[] protectedData, byte[] optionalEntropy)
        {
            if (!_payloads.TryGetValue(Convert.ToBase64String(protectedData), out var payload))
            {
                return ManagedWindowsCredentialProtectionResult.Failed("tampered");
            }

            LastUnprotectedPlaintext = payload.ToArray();
            return ManagedWindowsCredentialProtectionResult.Unprotected(LastUnprotectedPlaintext);
        }
    }

    private sealed class FakeWindowsAccountResolver : IWindowsAccountResolver
    {
        public Dictionary<string, WindowsAccountIdentity> Sids { get; } = new(StringComparer.OrdinalIgnoreCase);

        public WindowsAccountResolution ResolveCurrentUser()
        {
            return WindowsAccountResolution.NotFound("current user missing");
        }

        public WindowsAccountResolution ResolveAccount(string accountName)
        {
            return WindowsAccountResolution.NotFound("account lookup is not used");
        }

        public WindowsAccountResolution ResolveSid(string windowsSid)
        {
            return Sids.TryGetValue(windowsSid, out var identity)
                ? WindowsAccountResolution.Resolved(identity)
                : WindowsAccountResolution.NotFound("sid missing");
        }
    }

    private sealed class FakeClock : ISystemClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class RecordingCredentialFileSecurity : IManagedWindowsCredentialFileSecurity
    {
        public List<string> Paths { get; } = [];

        public void Apply(string filePath)
        {
            Paths.Add(filePath);
        }
    }

    private sealed class FakeDpapiNative : IWindowsDpapiManagedWindowsCredentialNativeApi
    {
        public bool IsWindows { get; init; } = true;
        public bool RunningAsLocalSystem { get; init; } = true;
        public bool FailUnprotect { get; init; }
        public int ProtectCalls { get; private set; }
        public int UnprotectCalls { get; private set; }
        public int LastProtectFlags { get; private set; }
        public int LastUnprotectFlags { get; private set; }

        public bool IsRunningAsLocalSystem()
        {
            return RunningAsLocalSystem;
        }

        public bool CryptProtectData(
            byte[] plaintext,
            byte[] optionalEntropy,
            int flags,
            out byte[] protectedData,
            out string errorMessage)
        {
            ProtectCalls++;
            LastProtectFlags = flags;
            protectedData = [9, 8, 7];
            errorMessage = string.Empty;
            return true;
        }

        public bool CryptUnprotectData(
            byte[] protectedData,
            byte[] optionalEntropy,
            int flags,
            out byte[] plaintext,
            out string errorMessage)
        {
            UnprotectCalls++;
            LastUnprotectFlags = flags;
            plaintext = FailUnprotect ? [] : [1, 2, 3];
            errorMessage = FailUnprotect ? "failed" : string.Empty;
            return !FailUnprotect;
        }
    }
}
