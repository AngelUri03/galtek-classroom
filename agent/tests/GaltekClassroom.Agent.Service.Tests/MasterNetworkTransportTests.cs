using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.Pairing;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Google.Protobuf;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class MasterNetworkTransportTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid ClientNetworkIdentityId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly Guid MasterNetworkIdentityId = Guid.Parse("cccccccc-dddd-eeee-ffff-000000000000");
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 27, 16, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.MasterNetworkTransport.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly RsaNetworkIdentityKeyStore _keys = new();

    [Fact]
    public async Task PairedMasterCertificateIsTrusted()
    {
        var clientIdentity = CreateClientIdentity();
        var master = CreateTrustedMaster("master-key", PairingStatus.Paired);
        await SaveTrustAsync(clientIdentity, master.Record);

        var resolved = await CreateResolver().ResolveAsync(
            MasterNetworkIdentityId,
            clientIdentity,
            CancellationToken.None);
        var pinned = new MasterCertificatePinningPolicy()
            .IsCertificateTrusted(master.Certificate, resolved.Master!, FixedNow);

        Assert.True(resolved.Trusted);
        Assert.True(pinned);
    }

    [Fact]
    public async Task UnpairedMasterIsRejectedBeforeTlsChannelIsCreated()
    {
        var clientIdentity = CreateClientIdentity();

        var resolved = await CreateResolver().ResolveAsync(
            MasterNetworkIdentityId,
            clientIdentity,
            CancellationToken.None);

        Assert.False(resolved.Trusted);
        Assert.Equal(PairingStatus.Unpaired, resolved.Status);
    }

    [Fact]
    public async Task RevokedMasterIsRejectedBeforeTlsChannelIsCreated()
    {
        var clientIdentity = CreateClientIdentity();
        var master = CreateTrustedMaster("master-key", PairingStatus.Revoked);
        await SaveTrustAsync(clientIdentity, master.Record);

        var resolved = await CreateResolver().ResolveAsync(
            MasterNetworkIdentityId,
            clientIdentity,
            CancellationToken.None);

        Assert.False(resolved.Trusted);
        Assert.Equal(PairingStatus.Revoked, resolved.Status);
    }

    [Fact]
    public async Task UnknownPeerCertificateIsRejectedByPinning()
    {
        var clientIdentity = CreateClientIdentity();
        var master = CreateTrustedMaster("master-key", PairingStatus.Paired);
        var attacker = CreateCertificate("attacker-key", "attacker");
        await SaveTrustAsync(clientIdentity, master.Record);

        var resolved = await CreateResolver().ResolveAsync(
            MasterNetworkIdentityId,
            clientIdentity,
            CancellationToken.None);
        var pinned = new MasterCertificatePinningPolicy()
            .IsCertificateTrusted(attacker, resolved.Master!, FixedNow);

        Assert.True(resolved.Trusted);
        Assert.False(pinned);
    }

    [Fact]
    public async Task ExpiredMasterCertificateIsRejectedByPinning()
    {
        var clientIdentity = CreateClientIdentity();
        var master = CreateTrustedMaster("master-key", PairingStatus.Paired);
        await SaveTrustAsync(clientIdentity, master.Record);
        var expired = _keys.CreateSelfSignedCertificate(
            "master-key",
            "master",
            FixedNow.AddDays(-10),
            FixedNow.AddDays(-1));

        var resolved = await CreateResolver().ResolveAsync(
            MasterNetworkIdentityId,
            clientIdentity,
            CancellationToken.None);
        var pinned = new MasterCertificatePinningPolicy()
            .IsCertificateTrusted(expired.Certificate, resolved.Master!, FixedNow);

        Assert.True(expired.Created);
        Assert.True(resolved.Trusted);
        Assert.False(pinned);
    }

    [Fact]
    public async Task MasterWithIncorrectFingerprintIsRejected()
    {
        var clientIdentity = CreateClientIdentity();
        var master = CreateTrustedMaster("master-key", PairingStatus.Paired);
        var tampered = master.Record with
        {
            MasterPublicKeyFingerprint = new string('0', 64)
        };

        await SaveTrustAsync(clientIdentity, tampered, expectFailure: true);
    }

    [Fact]
    public void ClientHelloCarriesNetworkIdentityAndNoSecrets()
    {
        var clientIdentity = CreateClientIdentity();
        var hello = new ClientHelloFactory(
            _keys,
            new FixedHostNameProvider("PC01"),
            new ClientCapabilityProvider(),
            new AgentVersionProvider(),
            new MutableClock(FixedNow))
            .Create(clientIdentity, new MasterConnectionOptions
            {
                DeviceId = "PC01",
                DisplayName = "PC01"
            });

        Assert.True(hello.Created);
        Assert.Equal(ClientNetworkIdentityId.ToString("D"), hello.Hello!.ClientNetworkIdentityId);
        Assert.Equal(InstallationId.ToString("D"), hello.Hello.ClientInstallationId);
        Assert.Equal(clientIdentity.PublicKeyFingerprint, hello.Hello.ClientPublicKeyFingerprint);
        Assert.Empty(hello.Hello.DeviceId);
        Assert.Equal("PC01", hello.Hello.DisplayName);
        Assert.Equal("PC01", hello.Hello.Hostname);
        Assert.NotEmpty(hello.Hello.AgentVersion);
        Assert.Contains(NetworkCapability.HeartbeatV1, hello.Hello.Capabilities);
        Assert.Contains(NetworkCapability.OperationFrameworkV1, hello.Hello.Capabilities);
        Assert.Contains(NetworkCapability.SessionAgentAvailable, hello.Hello.Capabilities);
        Assert.Contains(NetworkCapability.PowerControlV1, hello.Hello.Capabilities);
        Assert.Contains(NetworkCapability.OpenApplicationV1, hello.Hello.Capabilities);
        Assert.Contains(NetworkCapability.OpenUrlV1, hello.Hello.Capabilities);
        Assert.Contains(NetworkCapability.BrowserNavigationPolicyV1, hello.Hello.Capabilities);
        Assert.Contains(NetworkCapability.BrowserDownloadPolicyV1, hello.Hello.Capabilities);
        Assert.Contains(NetworkCapability.InputControlV1, hello.Hello.Capabilities);
        Assert.Contains(NetworkCapability.WindowsSessionStateV1, hello.Hello.Capabilities);
        Assert.Contains(NetworkCapability.ManagedCredentialProvisioningV1, hello.Hello.Capabilities);
        Assert.DoesNotContain(NetworkCapability.Unspecified, hello.Hello.Capabilities);
        Assert.DoesNotContain("private", hello.Hello.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownOperationReturnsNotImplementedAndDoesNotExecuteWindowsAction()
    {
        var dispatcher = new RemoteOperationDispatcher(
            [],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow));

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(new OperationRequest
        {
            OperationId = "operation-1",
            OperationType = NetworkOperationType.OpenUrl,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds()
        }, CancellationToken.None);

        Assert.Equal(OperationAcceptanceStatus.Accepted, dispatch.Accepted.Status);
        Assert.Equal(OperationExecutionStatus.Failed, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.OperationNotImplemented, dispatch.Result.ErrorCode);
    }

    [Fact]
    public async Task DuplicateOperationIdIsNotProcessedTwice()
    {
        var handler = new CountingOperationHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow));
        var request = new OperationRequest
        {
            OperationId = "operation-duplicate",
            OperationType = NetworkOperationType.LockInput,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds()
        };

        RemoteOperationDispatchResult first = await dispatcher.DispatchAsync(request, CancellationToken.None);
        RemoteOperationDispatchResult second = await dispatcher.DispatchAsync(request, CancellationToken.None);

        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(first.Result.CompletedAtUnixMs, second.Result.CompletedAtUnixMs);
    }

    [Fact]
    public async Task DuplicateDownloadPolicyOperationComparesTypedParameters()
    {
        var dispatcher = new RemoteOperationDispatcher(
            [],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow));
        OperationRequest request = DownloadPolicyRequest(
            "operation-download-policy",
            BrowserDownloadRestrictionMode.BlockDangerous);

        RemoteOperationDispatchResult first = await dispatcher.DispatchAsync(request, CancellationToken.None);
        RemoteOperationDispatchResult same = await dispatcher.DispatchAsync(
            DownloadPolicyRequest("operation-download-policy", BrowserDownloadRestrictionMode.BlockDangerous),
            CancellationToken.None);
        RemoteOperationDispatchResult different = await dispatcher.DispatchAsync(
            DownloadPolicyRequest("operation-download-policy", BrowserDownloadRestrictionMode.BlockAll),
            CancellationToken.None);

        Assert.False(first.Duplicate);
        Assert.True(same.Duplicate);
        Assert.True(different.Duplicate);
        Assert.Equal(NetworkOperationErrorCode.OperationNotImplemented, same.Result.ErrorCode);
        Assert.Equal(first.Result.CompletedAtUnixMs, same.Result.CompletedAtUnixMs);
        Assert.Equal(NetworkOperationErrorCode.OperationDuplicate, different.Result.ErrorCode);
    }

    [Fact]
    public async Task DuplicateProvisionManagedCredentialOperationComparesOnlySecretSafeMetadata()
    {
        var handler = new ProvisionCountingOperationHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow));

        RemoteOperationDispatchResult first = await dispatcher.DispatchAsync(
            ProvisionRequest("operation-provision", ManagedWindowsAccountId.Primary, "first-secret"),
            CancellationToken.None);
        RemoteOperationDispatchResult sameMetadataDifferentSecret = await dispatcher.DispatchAsync(
            ProvisionRequest("operation-provision", ManagedWindowsAccountId.Primary, "different-secret"),
            CancellationToken.None);

        Assert.False(first.Duplicate);
        Assert.True(sameMetadataDifferentSecret.Duplicate);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(first.Result.CompletedAtUnixMs, sameMetadataDifferentSecret.Result.CompletedAtUnixMs);
        Assert.Equal(OperationExecutionStatus.Success, sameMetadataDifferentSecret.Result.Status);
    }

    [Fact]
    public async Task DuplicateProvisionManagedCredentialOperationWithDifferentAccountConflicts()
    {
        var handler = new ProvisionCountingOperationHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow));

        _ = await dispatcher.DispatchAsync(
            ProvisionRequest("operation-provision-conflict", ManagedWindowsAccountId.Primary, "first-secret"),
            CancellationToken.None);
        RemoteOperationDispatchResult differentAccount = await dispatcher.DispatchAsync(
            ProvisionRequest("operation-provision-conflict", ManagedWindowsAccountId.Secondary, "second-secret"),
            CancellationToken.None);

        Assert.True(differentAccount.Duplicate);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(NetworkOperationErrorCode.OperationDuplicate, differentAccount.Result.ErrorCode);
    }

    [Fact]
    public async Task ProvisionManagedCredentialDedupeStateDoesNotStoreSecretRequest()
    {
        var dispatcher = new RemoteOperationDispatcher(
            [new ProvisionCountingOperationHandler()],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow));

        _ = await dispatcher.DispatchAsync(
            ProvisionRequest("operation-provision-state", ManagedWindowsAccountId.Primary, "secret-value"),
            CancellationToken.None);

        object operations = typeof(RemoteOperationDispatcher)
            .GetField("_operations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dispatcher)!;
        object state = ((System.Collections.IEnumerable)operations)
            .Cast<object>()
            .Select(entry => entry.GetType().GetProperty("Value")!.GetValue(entry)!)
            .Single();
        var stateMembers = state.GetType()
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(member => member.MemberType is MemberTypes.Field or MemberTypes.Property)
            .Select(member => member switch
            {
                FieldInfo field => field.FieldType,
                PropertyInfo property => property.PropertyType,
                _ => typeof(object)
            });

        Assert.DoesNotContain(typeof(OperationRequest), stateMembers);
        Assert.DoesNotContain("secret-value", state.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SHA", state.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", state.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevokedMasterIsRejectedBeforeOperationHandlerCanRun()
    {
        var clientIdentity = CreateClientIdentity();
        var master = CreateTrustedMaster("master-key", PairingStatus.Revoked);
        var handler = new CountingOperationHandler();
        await SaveTrustAsync(clientIdentity, master.Record);

        var resolved = await CreateResolver().ResolveAsync(
            MasterNetworkIdentityId,
            clientIdentity,
            CancellationToken.None);

        Assert.False(resolved.Trusted);
        Assert.Equal(PairingStatus.Revoked, resolved.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OperationDispatcher_CleansCompletedDedupeEntriesLazilyAfterRetention()
    {
        var clock = new MutableClock(FixedNow);
        var handler = new CountingOperationHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions
            {
                DedupeRetention = TimeSpan.FromSeconds(1),
                DedupeCleanupScanInterval = 1
            },
            clock);

        RemoteOperationDispatchResult first = await dispatcher.DispatchAsync(
            CreateOperationRequest("operation-expiring"),
            CancellationToken.None);

        clock.UtcNow = FixedNow.AddSeconds(2);
        _ = await dispatcher.DispatchAsync(
            CreateOperationRequest("operation-cleanup-trigger"),
            CancellationToken.None);

        RemoteOperationDispatchResult afterRetention = await dispatcher.DispatchAsync(
            CreateOperationRequest("operation-expiring"),
            CancellationToken.None);

        Assert.False(first.Duplicate);
        Assert.False(afterRetention.Duplicate);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task OperationDispatcher_TrimsCompletedDedupeEntriesWhenBoundIsExceeded()
    {
        var clock = new MutableClock(FixedNow);
        var handler = new CountingOperationHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions
            {
                DedupeRetention = TimeSpan.FromMinutes(30),
                MaxTrackedOperationIds = 2,
                DedupeCleanupScanInterval = 1
            },
            clock);

        _ = await dispatcher.DispatchAsync(CreateOperationRequest("operation-1"), CancellationToken.None);
        clock.UtcNow = FixedNow.AddMilliseconds(1);
        _ = await dispatcher.DispatchAsync(CreateOperationRequest("operation-2"), CancellationToken.None);
        clock.UtcNow = FixedNow.AddMilliseconds(2);
        _ = await dispatcher.DispatchAsync(CreateOperationRequest("operation-3"), CancellationToken.None);
        clock.UtcNow = FixedNow.AddMilliseconds(3);

        RemoteOperationDispatchResult evicted = await dispatcher.DispatchAsync(
            CreateOperationRequest("operation-1"),
            CancellationToken.None);

        Assert.False(evicted.Duplicate);
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task OperationDispatcher_WhenCommercialLicenseIsNotActive_RejectsBeforeHandler()
    {
        var handler = new CountingOperationHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow),
            new StaticLicenseStateProvider(LicenseState.Blocked(
                CommercialLicenseStatus.ActivationRequired,
                FixedNow,
                "Commercial license has not been resolved yet.")));

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(new OperationRequest
        {
            OperationId = "operation-license-blocked",
            OperationType = NetworkOperationType.LockInput,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds()
        }, CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.OperationRejected, dispatch.Result.ErrorCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OperationDispatcher_ProvisionManagedCredentialRequiresActiveCommercialLicense()
    {
        var handler = new ProvisionCountingOperationHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow),
            new StaticLicenseStateProvider(LicenseState.Blocked(
                CommercialLicenseStatus.LicenseExpired,
                FixedNow,
                "Commercial license is expired.")));

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            ProvisionRequest("operation-provision-license", ManagedWindowsAccountId.Primary, "secret"),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.OperationRejected, dispatch.Result.ErrorCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OperationDispatcher_ActiveCommercialLicenseAllowsProvisionManagedCredential()
    {
        var handler = new ProvisionCountingOperationHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow),
            new StaticLicenseStateProvider(LicenseState.ActiveState(
                "license-1",
                "school-1",
                [CommercialLicenseConstants.ClientRole],
                CommercialLicenseFeatures.Empty,
                FixedNow.AddDays(1),
                FixedNow)));

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            ProvisionRequest("operation-provision-active-license", ManagedWindowsAccountId.Primary, "secret"),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, dispatch.Result.Status);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void RemoteOperationLicensePolicy_OnlyUnlockInputBypassesActiveCommercialLicense()
    {
        var policy = RemoteOperationLicensePolicy.Default;

        Assert.False(policy.RequiresActiveCommercialLicense(NetworkOperationType.UnlockInput));
        Assert.True(policy.RequiresActiveCommercialLicense(NetworkOperationType.ProvisionManagedCredential));
        Assert.True(policy.RequiresActiveCommercialLicense(NetworkOperationType.LockInput));
    }

    [Fact]
    public void ReconnectBackoffIncreasesAndCanBeReset()
    {
        var backoff = new MasterConnectionBackoff(
        [
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5)
        ]);

        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(5), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(5), backoff.NextDelay());

        backoff.Reset();

        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay());
    }

    [Fact]
    public void ReconnectBackoff_AddsBoundedJitterWithoutRemovingBaseDelay()
    {
        var backoff = new MasterConnectionBackoff(
        [
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5)
        ],
        new ScriptedReconnectJitter(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750)),
        TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromMilliseconds(2250), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromMilliseconds(5750), backoff.NextDelay());
    }

    [Fact]
    public void InitialConnectJitter_IsSmallAndCanBeZero()
    {
        var zero = new MasterConnectionBackoff(
            [TimeSpan.FromSeconds(2)],
            new ScriptedReconnectJitter(TimeSpan.Zero),
            TimeSpan.FromSeconds(1));
        var delayed = new MasterConnectionBackoff(
            [TimeSpan.FromSeconds(2)],
            new ScriptedReconnectJitter(TimeSpan.FromSeconds(2)),
            TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.Zero, zero.InitialDelay(TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(2), delayed.InitialDelay(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void InitialConnectJitter_AllowsMultipleClientsToAvoidPerfectSynchronization()
    {
        var plannedInitialDelays = new[]
        {
            new MasterConnectionBackoff(
                [TimeSpan.FromSeconds(2)],
                new ScriptedReconnectJitter(TimeSpan.Zero),
                TimeSpan.FromSeconds(1)),
            new MasterConnectionBackoff(
                [TimeSpan.FromSeconds(2)],
                new ScriptedReconnectJitter(TimeSpan.FromMilliseconds(500)),
                TimeSpan.FromSeconds(1)),
            new MasterConnectionBackoff(
                [TimeSpan.FromSeconds(2)],
                new ScriptedReconnectJitter(TimeSpan.FromMilliseconds(1250)),
                TimeSpan.FromSeconds(1))
        }.Select(backoff => backoff.InitialDelay(TimeSpan.FromSeconds(2))).ToArray();

        Assert.All(plannedInitialDelays, delay => Assert.InRange(
            delay,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2)));
        Assert.True(plannedInitialDelays.Distinct().Count() > 1);
    }

    private static OperationRequest CreateOperationRequest(string operationId)
    {
        return new OperationRequest
        {
            OperationId = operationId,
            OperationType = NetworkOperationType.LockInput,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds()
        };
    }

    private static OperationRequest DownloadPolicyRequest(
        string operationId,
        BrowserDownloadRestrictionMode restrictionMode)
    {
        return new OperationRequest
        {
            OperationId = operationId,
            OperationType = NetworkOperationType.ApplyBrowserDownloadPolicy,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds(),
            ApplyBrowserDownloadPolicy = new ApplyBrowserDownloadPolicyOperationParameters
            {
                PolicyId = "download-policy-1",
                PolicyVersion = 1,
                RestrictionMode = restrictionMode,
                AccountScope = BrowserPolicyAccountScope.Any
            }
        };
    }

    private static OperationRequest ProvisionRequest(
        string operationId,
        ManagedWindowsAccountId accountId,
        string password)
    {
        return new OperationRequest
        {
            OperationId = operationId,
            OperationType = NetworkOperationType.ProvisionManagedCredential,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds(),
            ProvisionManagedCredential = new ProvisionManagedCredentialOperationParameters
            {
                AccountId = accountId,
                PasswordUtf16Le = ByteString.CopyFrom(System.Text.Encoding.Unicode.GetBytes(password))
            }
        };
    }

    public void Dispose()
    {
        _keys.Dispose();

        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private TrustedMasterResolver CreateResolver()
    {
        return new TrustedMasterResolver(CreateTrustStore());
    }

    private ClientTrustStore CreateTrustStore()
    {
        return new ClientTrustStore(
            new ClientTrustStoreOptions(_dataDirectory),
            new NoOpNetworkIdentityFileSecurity());
    }

    private async Task SaveTrustAsync(
        NetworkIdentityMetadata clientIdentity,
        AuthorizedMasterTrustRecord master,
        bool expectFailure = false)
    {
        var document = new ClientTrustDocument
        {
            SchemaVersion = PairingConstants.SchemaVersion,
            ClientNetworkIdentityId = clientIdentity.NetworkIdentityId,
            ClientInstallationId = clientIdentity.InstallationId,
            ClientPublicKeyFingerprint = clientIdentity.PublicKeyFingerprint,
            AuthorizedMasters = [master],
            ConsumedChallenges = []
        };

        if (expectFailure)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateTrustStore().SaveAsync(document, CancellationToken.None));
            return;
        }

        await CreateTrustStore().SaveAsync(document, CancellationToken.None);
    }

    private NetworkIdentityMetadata CreateClientIdentity()
    {
        var descriptor = NetworkIdentityKeyDescriptor.ForInstallation(InstallationId);
        if (!_keys.Exists(descriptor.KeyName))
        {
            _keys.Create(descriptor.KeyName);
        }

        var publicKey = _keys.GetPublicKey(descriptor.KeyName);
        return NetworkIdentityMetadata.Create(
            ClientNetworkIdentityId,
            InstallationId,
            descriptor,
            publicKey.PublicKeyFingerprint!,
            FixedNow);
    }

    private TrustedMaster CreateTrustedMaster(string keyName, PairingStatus status)
    {
        var certificate = CreateCertificate(keyName, "master");
        var publicKey = _keys.GetPublicKey(keyName);
        var record = new AuthorizedMasterTrustRecord
        {
            SchemaVersion = PairingConstants.SchemaVersion,
            Status = status.ToCode(),
            MasterNetworkIdentityId = MasterNetworkIdentityId,
            ClientNetworkIdentityId = ClientNetworkIdentityId,
            ClientInstallationId = InstallationId,
            MasterPublicKeyFingerprint = publicKey.PublicKeyFingerprint!,
            ClientPublicKeyFingerprint = CreateClientIdentity().PublicKeyFingerprint,
            MasterPublicKeySubjectPublicKeyInfoBase64 = publicKey.SubjectPublicKeyInfoBase64!,
            PairedAtUtc = FixedNow,
            RevokedAtUtc = status == PairingStatus.Revoked ? FixedNow.AddMinutes(1) : null,
            PairedChallengeId = Guid.Parse("dddddddd-eeee-ffff-0000-111111111111")
        };

        return new TrustedMaster(record, certificate);
    }

    private X509Certificate2 CreateCertificate(string keyName, string subject)
    {
        if (!_keys.Exists(keyName))
        {
            _keys.Create(keyName);
        }

        var result = _keys.CreateSelfSignedCertificate(
            keyName,
            subject,
            FixedNow.AddMinutes(-1),
            FixedNow.AddDays(7));

        Assert.True(result.Created);
        return result.Certificate!;
    }

    private sealed record TrustedMaster(
        AuthorizedMasterTrustRecord Record,
        X509Certificate2 Certificate);

    private sealed class MutableClock : ISystemClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FixedHostNameProvider : IHostNameProvider
    {
        private readonly string _hostName;

        public FixedHostNameProvider(string hostName)
        {
            _hostName = hostName;
        }

        public string GetHostName()
        {
            return _hostName;
        }
    }

    private sealed class CountingOperationHandler : IRemoteOperationHandler
    {
        public int Calls { get; private set; }

        public NetworkOperationType OperationType => NetworkOperationType.LockInput;

        public Task<RemoteOperationHandlerResult> HandleAsync(
            OperationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(RemoteOperationHandlerResult.NotImplemented());
        }
    }

    private sealed class ProvisionCountingOperationHandler : IRemoteOperationHandler
    {
        public int Calls { get; private set; }

        public NetworkOperationType OperationType => NetworkOperationType.ProvisionManagedCredential;

        public Task<RemoteOperationHandlerResult> HandleAsync(
            OperationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(RemoteOperationHandlerResult.Success("provisioned"));
        }
    }

    private sealed class StaticLicenseStateProvider : ILicenseStateProvider
    {
        public StaticLicenseStateProvider(LicenseState currentState)
        {
            CurrentState = currentState;
        }

        public LicenseState CurrentState { get; }
    }

    private sealed class RsaNetworkIdentityKeyStore : INetworkIdentityKeyStore, IDisposable
    {
        private readonly Dictionary<string, RSA> _keys = new(StringComparer.Ordinal);

        public bool Exists(string keyName)
        {
            return _keys.ContainsKey(keyName);
        }

        public NetworkIdentityKeyCreationResult Create(string keyName)
        {
            if (_keys.ContainsKey(keyName))
            {
                return NetworkIdentityKeyCreationResult.AlreadyExists(keyName);
            }

            _keys[keyName] = RSA.Create(NetworkIdentityConstants.RsaKeySizeBits);
            return NetworkIdentityKeyCreationResult.Success(Fingerprint(_keys[keyName]));
        }

        public NetworkIdentityKeyLookupResult GetPublicKeyFingerprint(string keyName)
        {
            return _keys.TryGetValue(keyName, out var key)
                ? NetworkIdentityKeyLookupResult.Found(Fingerprint(key))
                : NetworkIdentityKeyLookupResult.Missing(keyName);
        }

        public NetworkIdentityPublicKeyResult GetPublicKey(string keyName)
        {
            return _keys.TryGetValue(keyName, out var key)
                ? NetworkIdentityPublicKeyResult.Found(
                    Fingerprint(key),
                    Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))
                : NetworkIdentityPublicKeyResult.Missing(keyName);
        }

        public NetworkIdentitySignatureResult Sign(string keyName, byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);

            if (!_keys.TryGetValue(keyName, out var key))
            {
                return NetworkIdentitySignatureResult.Missing(keyName);
            }

            return NetworkIdentitySignatureResult.Success(Convert.ToBase64String(
                key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
        }

        public NetworkIdentityCertificateResult CreateSelfSignedCertificate(
            string keyName,
            string subjectName,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter)
        {
            if (!_keys.TryGetValue(keyName, out var key))
            {
                return NetworkIdentityCertificateResult.Missing(keyName);
            }

            var request = new CertificateRequest(
                $"CN={subjectName}",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true));

            return NetworkIdentityCertificateResult.Success(
                Fingerprint(key),
                request.CreateSelfSigned(notBefore, notAfter));
        }

        public NetworkIdentityKeyDeleteResult Delete(string keyName)
        {
            if (!_keys.Remove(keyName, out var key))
            {
                return NetworkIdentityKeyDeleteResult.Missing();
            }

            key.Dispose();
            return NetworkIdentityKeyDeleteResult.Success();
        }

        public void Dispose()
        {
            foreach (var key in _keys.Values)
            {
                key.Dispose();
            }

            _keys.Clear();
        }

        private static string Fingerprint(RSA key)
        {
            return Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        }
    }

    private sealed class ScriptedReconnectJitter : IReconnectJitter
    {
        private readonly Queue<TimeSpan> _values;

        public ScriptedReconnectJitter(params TimeSpan[] values)
        {
            _values = new Queue<TimeSpan>(values);
        }

        public TimeSpan NextJitter(TimeSpan maxJitter)
        {
            if (_values.Count == 0 || maxJitter <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            var value = _values.Dequeue();
            if (value < TimeSpan.Zero || value > maxJitter)
            {
                throw new InvalidOperationException("Scripted jitter value is outside the requested bound.");
            }

            return value;
        }
    }
}
