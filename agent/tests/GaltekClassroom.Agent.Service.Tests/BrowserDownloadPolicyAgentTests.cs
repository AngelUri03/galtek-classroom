using GaltekClassroom.Agent.Service.BrowserPolicy;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class BrowserDownloadPolicyAgentTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyService_FirstExplicitApplyWithEmptyParentsWritesDwordAndPersistsState()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake();
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            Compile(BrowserDownloadRestrictionMode.BlockAll),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(registry.Snapshot.Chrome.Value.MatchesDesired(3));
        Assert.True(registry.Snapshot.Edge.Value.MatchesDesired(3));
        Assert.Equal(2, registry.ParentAclRewriteCalls);
        Assert.Equal(0, registry.ChildAclRewriteCalls);
        Assert.True(File.Exists(Path.Combine(temp.Path, BrowserDownloadPolicyStateStore.StateFileName)));
        Assert.False(File.Exists(Path.Combine(temp.Path, BrowserDownloadPolicyStateStore.JournalFileName)));
    }

    [Fact]
    public async Task ApplyService_UnknownUserLevelDownloadRestrictionsFailsConflict()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake
        {
            Snapshot = Snapshot(Value(1), Value(null))
        };
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            Compile(BrowserDownloadRestrictionMode.BlockAll),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserDownloadPolicyExternalConflict, result.ErrorCode);
        Assert.Equal(0, registry.WriteCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ApplyService_MachineLevelDownloadRestrictionsFailsConflict(bool chromeMachine, bool edgeMachine)
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake
        {
            HasChromeMachineDownloadRestrictions = chromeMachine,
            HasEdgeMachineDownloadRestrictions = edgeMachine
        };
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            Compile(BrowserDownloadRestrictionMode.BlockDangerous),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserDownloadPolicyExternalConflict, result.ErrorCode);
    }

    [Fact]
    public async Task ApplyService_OtherMachinePoliciesAreNotConflicts()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake { HasOtherMachinePolicy = true };
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            Compile(BrowserDownloadRestrictionMode.BlockDangerous),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task ApplyService_StateAndMatchingRegistryAllowUpdate()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake();
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);
        Assert.True((await service.ApplyAsync("S-1-5-21-1", Compile(BrowserDownloadRestrictionMode.BlockDangerous), CancellationToken.None)).Succeeded);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            Compile(BrowserDownloadRestrictionMode.BlockPotentiallyDangerous),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(registry.Snapshot.Chrome.Value.MatchesDesired(2));
        Assert.True(registry.Snapshot.Edge.Value.MatchesDesired(2));
    }

    [Fact]
    public async Task ApplyService_StateAndDivergentRegistryFailsConflict()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake();
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);
        Assert.True((await service.ApplyAsync("S-1-5-21-1", Compile(BrowserDownloadRestrictionMode.BlockDangerous), CancellationToken.None)).Succeeded);
        registry.Snapshot = Snapshot(Value(3), Value(1));

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            Compile(BrowserDownloadRestrictionMode.BlockPotentiallyDangerous),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserDownloadPolicyExternalConflict, result.ErrorCode);
    }

    [Theory]
    [InlineData("ExternalValue", "")]
    [InlineData("", "Recommended")]
    public async Task ApplyService_UnknownParentContentFailsBeforeAclTakeover(string valueName, string subKeyName)
    {
        using var temp = new TempDirectory();
        BrowserDownloadBrowserSnapshot chrome = EmptyBrowser(parentExists: true);
        chrome = chrome with
        {
            Parent = chrome.Parent with
            {
                ValueNames = string.IsNullOrEmpty(valueName) ? [] : [valueName],
                SubKeyNames = string.IsNullOrEmpty(subKeyName) ? [] : [subKeyName]
            }
        };
        var registry = new DownloadRegistryFake { Snapshot = new BrowserDownloadRegistrySnapshot(chrome, EmptyBrowser()) };
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            Compile(BrowserDownloadRestrictionMode.BlockAll),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserDownloadPolicyExternalConflict, result.ErrorCode);
        Assert.Equal(0, registry.ParentAclRewriteCalls);
    }

    [Fact]
    public async Task ApplyService_GaltekOwnedNavigationSubkeysAreAllowedButUnprovedOnesConflict()
    {
        BrowserDownloadBrowserSnapshot parentWithNavigationSubkeys = EmptyBrowser(parentExists: true) with
        {
            Parent = EmptyBrowser(parentExists: true).Parent with { SubKeyNames = ["URLBlocklist", "URLAllowlist"] }
        };
        using var allowedTemp = new TempDirectory();
        var allowedRegistry = new DownloadRegistryFake
        {
            Snapshot = new BrowserDownloadRegistrySnapshot(
                parentWithNavigationSubkeys,
                EmptyBrowser())
        };
        BrowserDownloadPolicyApplyService allowed = CreateService(
            allowedRegistry,
            allowedTemp.Path,
            new NavigationOwnershipFake(true));

        Assert.True((await allowed.ApplyAsync("S-1-5-21-1", Compile(BrowserDownloadRestrictionMode.BlockAll), CancellationToken.None)).Succeeded);

        using var deniedTemp = new TempDirectory();
        var deniedRegistry = new DownloadRegistryFake
        {
            Snapshot = new BrowserDownloadRegistrySnapshot(parentWithNavigationSubkeys, EmptyBrowser())
        };
        BrowserDownloadPolicyApplyService denied = CreateService(
            deniedRegistry,
            deniedTemp.Path,
            new NavigationOwnershipFake(false));

        BrowserDownloadPolicyApplyResult result = await denied.ApplyAsync(
            "S-1-5-21-1",
            Compile(BrowserDownloadRestrictionMode.BlockAll),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserDownloadPolicyExternalConflict, result.ErrorCode);
    }

    [Theory]
    [InlineData(BrowserDownloadRestrictionMode.NoSpecialRestrictions, 0)]
    [InlineData(BrowserDownloadRestrictionMode.BlockDangerous, 1)]
    [InlineData(BrowserDownloadRestrictionMode.BlockPotentiallyDangerous, 2)]
    [InlineData(BrowserDownloadRestrictionMode.BlockAll, 3)]
    [InlineData(BrowserDownloadRestrictionMode.BlockMalicious, 4)]
    public async Task ApplyService_ExplicitModesWriteDwordValueToBothBrowsers(
        BrowserDownloadRestrictionMode mode,
        int expectedValue)
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake();
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync("S-1-5-21-1", Compile(mode), CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("DWord", registry.Snapshot.Chrome.Value.ValueKind);
        Assert.True(registry.Snapshot.Chrome.Value.MatchesDesired(expectedValue));
        Assert.True(registry.Snapshot.Edge.Value.MatchesDesired(expectedValue));
    }

    [Fact]
    public async Task ApplyService_EdgeFailureAfterChromeRollsBackAndReturnsApplyFailed()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake { FailWriteBrowser = BrowserDownloadPolicyBrowser.Edge };
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            Compile(BrowserDownloadRestrictionMode.BlockAll),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserDownloadPolicyApplyFailed, result.ErrorCode);
        Assert.False(registry.Snapshot.Chrome.Value.Exists);
        Assert.False(registry.Snapshot.Edge.Value.Exists);
        Assert.True(registry.RollbackCalls >= 2);
    }

    [Fact]
    public async Task ApplyService_RollbackFailureReturnsRollbackFailed()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake
        {
            FailWriteBrowser = BrowserDownloadPolicyBrowser.Edge,
            FailRollback = true
        };
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            Compile(BrowserDownloadRestrictionMode.BlockAll),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserDownloadPolicyRollbackFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ApplyService_SameHashAndRegistryAndAclDoNotRewrite()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake();
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);
        CompiledBrowserDownloadPolicy policy = Compile(BrowserDownloadRestrictionMode.BlockAll);

        Assert.True((await service.ApplyAsync("S-1-5-21-1", policy, CancellationToken.None)).Succeeded);
        int writes = registry.WriteCalls;
        BrowserDownloadPolicyApplyResult second = await service.ApplyAsync("S-1-5-21-1", policy, CancellationToken.None);

        Assert.True(second.Succeeded);
        Assert.True(second.NoChange);
        Assert.Equal(writes, registry.WriteCalls);
    }

    [Fact]
    public async Task ApplyService_ImplicitRemoveWithoutStateAndWithoutValuesIsNoChange()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake();
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            CompileImplicitRemoval(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.NoChange);
        Assert.Equal(0, registry.WriteCalls);
    }

    [Fact]
    public async Task ApplyService_ImplicitRemoveUnknownExistingValueFailsConflict()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake { Snapshot = Snapshot(Value(2), Value(null)) };
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            CompileImplicitRemoval(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserDownloadPolicyExternalConflict, result.ErrorCode);
    }

    [Fact]
    public async Task ApplyService_ImplicitRemoveOwnedValueRemovesBothBrowsers()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake();
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);
        Assert.True((await service.ApplyAsync("S-1-5-21-1", Compile(BrowserDownloadRestrictionMode.BlockAll), CancellationToken.None)).Succeeded);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            CompileImplicitRemoval(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(registry.Snapshot.Chrome.Value.Exists);
        Assert.False(registry.Snapshot.Edge.Value.Exists);
    }

    [Fact]
    public async Task ApplyService_ImplicitRemoveRestoresPreviousAclWhenGaltekHardenedExistingParent()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake
        {
            Snapshot = new BrowserDownloadRegistrySnapshot(
                EmptyBrowser(parentExists: true),
                EmptyBrowser(parentExists: true))
        };
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);
        Assert.True((await service.ApplyAsync("S-1-5-21-1", Compile(BrowserDownloadRestrictionMode.BlockAll), CancellationToken.None)).Succeeded);

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            CompileImplicitRemoval(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, registry.ParentAclRestoreCalls);
    }

    [Fact]
    public async Task ApplyService_ImplicitRemovePreservesExternalParentContentAndFlagsUnsafeAclRestore()
    {
        using var temp = new TempDirectory();
        var registry = new DownloadRegistryFake();
        BrowserDownloadPolicyApplyService service = CreateService(registry, temp.Path);
        Assert.True((await service.ApplyAsync("S-1-5-21-1", Compile(BrowserDownloadRestrictionMode.BlockAll), CancellationToken.None)).Succeeded);
        registry.AddParentValue(BrowserDownloadPolicyBrowser.Chrome, "ExternalPolicy");

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            CompileImplicitRemoval(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserDownloadPolicyRecoveryRequired, result.ErrorCode);
        Assert.Contains("ExternalPolicy", registry.Snapshot.Chrome.Parent.ValueNames);
        Assert.False(registry.Snapshot.Chrome.Value.Exists);
        Assert.True(File.Exists(Path.Combine(temp.Path, BrowserDownloadPolicyStateStore.JournalFileName)));
    }

    [Fact]
    public async Task ApplyService_RecoveryFinalizesWhenRegistryAlreadyDesired()
    {
        using var temp = new TempDirectory();
        var stateStore = new BrowserDownloadPolicyStateStore(temp.Path);
        CompiledBrowserDownloadPolicy desired = Compile(BrowserDownloadRestrictionMode.BlockAll);
        BrowserDownloadPolicyStateEntry desiredState = State("S-1-5-21-1", desired, hardened: false);
        await stateStore.SaveJournalAsync(
            new BrowserDownloadPolicyApplyJournalEntry(
                "S-1-5-21-1",
                desired.ContentHash,
                null,
                desiredState,
                Snapshot(Value(null), Value(null)),
                Snapshot(Value(3), Value(3)),
                "applying",
                FixedNow),
            CancellationToken.None);
        var registry = new DownloadRegistryFake { Snapshot = Snapshot(Value(3), Value(3)) };
        var service = new BrowserDownloadPolicyApplyService(
            registry,
            stateStore,
            new NavigationOwnershipFake(true),
            new MutableClock(FixedNow));

        BrowserDownloadPolicyApplyResult result = await service.ApplyAsync(
            "S-1-5-21-1",
            desired,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(File.Exists(Path.Combine(temp.Path, BrowserDownloadPolicyStateStore.JournalFileName)));
    }

    [Fact]
    public async Task Handler_AnyResolvesInteractiveUserAndPrimarySecondaryRemainUnresolved()
    {
        using var temp = new TempDirectory();
        var resolver = new StaticUserResolver("S-1-5-21-1");
        var registry = new DownloadRegistryFake();
        var handler = new ApplyBrowserDownloadPolicyOperationHandler(
            new ChromiumDownloadPolicyCompiler(),
            resolver,
            CreateService(registry, temp.Path),
            NullLogger<ApplyBrowserDownloadPolicyOperationHandler>.Instance);

        RemoteOperationHandlerResult any = await handler.HandleAsync(
            ApplyRequest(DownloadPolicy(BrowserDownloadRestrictionMode.BlockDangerous)),
            CancellationToken.None);
        OperationRequest primaryRequest = ApplyRequest(DownloadPolicy(BrowserDownloadRestrictionMode.BlockDangerous));
        primaryRequest.ApplyBrowserDownloadPolicy.AccountScope = BrowserPolicyAccountScope.Primary;
        RemoteOperationHandlerResult primary = await handler.HandleAsync(primaryRequest, CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, any.Status);
        Assert.Equal("S-1-5-21-1", resolver.LastResolvedSid);
        Assert.Equal(NetworkOperationErrorCode.BrowserAccountScopeUnresolved, primary.ErrorCode);
    }

    [Fact]
    public async Task Handler_UserUnavailableReturnsStructuredBrowserError()
    {
        using var temp = new TempDirectory();
        var handler = new ApplyBrowserDownloadPolicyOperationHandler(
            new ChromiumDownloadPolicyCompiler(),
            new StaticUserResolver(null),
            CreateService(new DownloadRegistryFake(), temp.Path),
            NullLogger<ApplyBrowserDownloadPolicyOperationHandler>.Instance);

        RemoteOperationHandlerResult result = await handler.HandleAsync(
            ApplyRequest(DownloadPolicy(BrowserDownloadRestrictionMode.BlockDangerous)),
            CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.BrowserPolicyUserUnavailable, result.ErrorCode);
    }

    [Fact]
    public void DownloadHandlerAndApplyServiceDoNotCreateHostedTimersOrUseSessionCommand()
    {
        Assert.False(typeof(IHostedService).IsAssignableFrom(typeof(ApplyBrowserDownloadPolicyOperationHandler)));
        Assert.False(typeof(IHostedService).IsAssignableFrom(typeof(BrowserDownloadPolicyApplyService)));
        Assert.False(typeof(IHostedService).IsAssignableFrom(typeof(WindowsBrowserDownloadPolicyRegistryStore)));
        Assert.DoesNotContain("SessionCommand", typeof(ApplyBrowserDownloadPolicyOperationHandler).AssemblyQualifiedName);
    }

    private static BrowserDownloadPolicyApplyService CreateService(
        DownloadRegistryFake registry,
        string dataDirectory,
        IBrowserNavigationOwnershipReader? navigationOwnership = null)
    {
        return new BrowserDownloadPolicyApplyService(
            registry,
            new BrowserDownloadPolicyStateStore(dataDirectory),
            navigationOwnership ?? new NavigationOwnershipFake(true),
            new MutableClock(FixedNow));
    }

    private static CompiledBrowserDownloadPolicy Compile(BrowserDownloadRestrictionMode mode)
    {
        ChromiumDownloadPolicyCompilerResult result = new ChromiumDownloadPolicyCompiler().Compile(DownloadPolicy(mode));
        Assert.True(result.Succeeded, result.Message);
        return result.Policy!;
    }

    private static CompiledBrowserDownloadPolicy CompileImplicitRemoval()
    {
        ChromiumDownloadPolicyCompilerResult result = new ChromiumDownloadPolicyCompiler().Compile(
            new ApplyBrowserDownloadPolicyOperationParameters
            {
                ImplicitNoSpecialRestrictions = true,
                RestrictionMode = BrowserDownloadRestrictionMode.NoSpecialRestrictions,
                AccountScope = BrowserPolicyAccountScope.Any
            });
        Assert.True(result.Succeeded, result.Message);
        return result.Policy!;
    }

    private static ApplyBrowserDownloadPolicyOperationParameters DownloadPolicy(BrowserDownloadRestrictionMode mode)
    {
        return new ApplyBrowserDownloadPolicyOperationParameters
        {
            PolicyId = "download-policy-1",
            PolicyVersion = 1,
            RestrictionMode = mode,
            AccountScope = BrowserPolicyAccountScope.Any
        };
    }

    private static OperationRequest ApplyRequest(ApplyBrowserDownloadPolicyOperationParameters parameters)
    {
        return new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.ApplyBrowserDownloadPolicy,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            ApplyBrowserDownloadPolicy = parameters
        };
    }

    private static BrowserDownloadPolicyStateEntry State(
        string windowsSid,
        CompiledBrowserDownloadPolicy policy,
        bool hardened)
    {
        var browserState = new BrowserDownloadPolicyBrowserState(true, hardened, hardened ? "previous-sddl" : null);
        return new BrowserDownloadPolicyStateEntry(
            windowsSid,
            policy.PolicyId,
            policy.PolicyVersion,
            policy.ContentHash,
            policy.RemoveGaltekPolicy,
            policy.NativeDownloadRestrictionsValue,
            FixedNow,
            browserState,
            browserState);
    }

    private static BrowserDownloadBrowserSnapshot EmptyBrowser(bool parentExists = false)
    {
        return new BrowserDownloadBrowserSnapshot(
            BrowserDownloadBrowserValue.Missing,
            new BrowserDownloadParentSnapshot(parentExists, [], [], false, parentExists ? "sddl" : null));
    }

    private static BrowserDownloadBrowserSnapshot Value(int? value)
    {
        return value is null
            ? EmptyBrowser(parentExists: true)
            : new BrowserDownloadBrowserSnapshot(
                new BrowserDownloadBrowserValue(true, "DWord", value),
                new BrowserDownloadParentSnapshot(true, ["DownloadRestrictions"], [], true, "sddl"));
    }

    private static BrowserDownloadRegistrySnapshot Snapshot(
        BrowserDownloadBrowserSnapshot chrome,
        BrowserDownloadBrowserSnapshot edge)
    {
        return new BrowserDownloadRegistrySnapshot(chrome, edge);
    }

    private sealed class DownloadRegistryFake : IBrowserDownloadPolicyRegistryStore
    {
        public BrowserDownloadRegistrySnapshot Snapshot { get; set; } = Snapshot(EmptyBrowser(), EmptyBrowser());

        public bool HasChromeMachineDownloadRestrictions { get; set; }

        public bool HasEdgeMachineDownloadRestrictions { get; set; }

        public bool HasOtherMachinePolicy { get; set; }

        public BrowserDownloadPolicyBrowser? FailWriteBrowser { get; set; }

        public bool FailRollback { get; set; }

        public int WriteCalls { get; private set; }

        public int RollbackCalls { get; private set; }

        public int ParentAclRewriteCalls { get; private set; }

        public int ParentAclRestoreCalls { get; private set; }

        public int ChildAclRewriteCalls { get; private set; }

        public Task<BrowserDownloadRegistrySnapshot> ReadUserPolicyAsync(
            string windowsSid,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot);
        }

        public Task<bool> HasMachineLevelDownloadRestrictionsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = HasOtherMachinePolicy;
            return Task.FromResult(HasChromeMachineDownloadRestrictions || HasEdgeMachineDownloadRestrictions);
        }

        public Task<BrowserDownloadParentPlan> PlanParentAclAsync(
            string windowsSid,
            BrowserDownloadPolicyBrowser browser,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BrowserDownloadParentSnapshot parent = Browser(browser).Parent;
            return Task.FromResult(new BrowserDownloadParentPlan(
                parent.Exists,
                !parent.Exists || !parent.AclSafe,
                parent.Exists && !parent.AclSafe ? parent.SecurityDescriptor : null));
        }

        public Task WriteDownloadRestrictionsAsync(
            string windowsSid,
            BrowserDownloadPolicyBrowser browser,
            int? nativeValue,
            BrowserDownloadParentPlan parentPlan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailWriteBrowser == browser)
            {
                throw new BrowserDownloadPolicyRegistryException(
                    NetworkOperationErrorCode.BrowserDownloadPolicyApplyFailed,
                    "Write failed.");
            }

            WriteCalls++;
            if (parentPlan.ParentAclHardenedByGaltek)
            {
                ParentAclRewriteCalls++;
            }

            SetBrowser(browser, WithValue(Browser(browser), nativeValue, parentPlan.ParentAclHardenedByGaltek));
            return Task.CompletedTask;
        }

        public Task RestoreBrowserAsync(
            string windowsSid,
            BrowserDownloadPolicyBrowser browser,
            BrowserDownloadBrowserSnapshot snapshot,
            bool restoreParentAcl,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RollbackCalls++;
            if (FailRollback)
            {
                throw new BrowserDownloadPolicyRegistryException(
                    NetworkOperationErrorCode.BrowserDownloadPolicyRollbackFailed,
                    "Rollback failed.");
            }

            if (restoreParentAcl)
            {
                ParentAclRestoreCalls++;
            }

            SetBrowser(browser, snapshot);
            return Task.CompletedTask;
        }

        public void AddParentValue(BrowserDownloadPolicyBrowser browser, string valueName)
        {
            BrowserDownloadBrowserSnapshot current = Browser(browser);
            SetBrowser(browser, current with
            {
                Parent = current.Parent with
                {
                    Exists = true,
                    ValueNames = current.Parent.ValueNames.Append(valueName).Distinct(StringComparer.Ordinal).ToArray()
                }
            });
        }

        private BrowserDownloadBrowserSnapshot Browser(BrowserDownloadPolicyBrowser browser)
        {
            return browser == BrowserDownloadPolicyBrowser.Chrome ? Snapshot.Chrome : Snapshot.Edge;
        }

        private void SetBrowser(BrowserDownloadPolicyBrowser browser, BrowserDownloadBrowserSnapshot snapshot)
        {
            Snapshot = browser == BrowserDownloadPolicyBrowser.Chrome
                ? Snapshot with { Chrome = snapshot }
                : Snapshot with { Edge = snapshot };
        }

        private static BrowserDownloadBrowserSnapshot WithValue(
            BrowserDownloadBrowserSnapshot current,
            int? nativeValue,
            bool aclHardened)
        {
            string[] values = nativeValue is null
                ? current.Parent.ValueNames.Where(name => !string.Equals(name, "DownloadRestrictions", StringComparison.Ordinal)).ToArray()
                : current.Parent.ValueNames.Append("DownloadRestrictions").Distinct(StringComparer.Ordinal).ToArray();
            return new BrowserDownloadBrowserSnapshot(
                nativeValue is null
                    ? BrowserDownloadBrowserValue.Missing
                    : new BrowserDownloadBrowserValue(true, "DWord", nativeValue),
                current.Parent with
                {
                    Exists = true,
                    ValueNames = values,
                    AclSafe = aclHardened || current.Parent.AclSafe,
                    SecurityDescriptor = current.Parent.SecurityDescriptor ?? "created-sddl"
                });
        }
    }

    private sealed class NavigationOwnershipFake : IBrowserNavigationOwnershipReader
    {
        private readonly bool _owned;

        public NavigationOwnershipFake(bool owned)
        {
            _owned = owned;
        }

        public Task<bool> IsGaltekOwnedNavigationSubkeyAsync(
            string windowsSid,
            string subKeyName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_owned);
        }
    }

    private sealed class StaticUserResolver : IInteractiveUserIdentityResolver
    {
        private readonly string? _sid;

        public StaticUserResolver(string? sid)
        {
            _sid = sid;
        }

        public string? LastResolvedSid { get; private set; }

        public Task<InteractiveUserIdentityResult> ResolveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastResolvedSid = _sid;
            return Task.FromResult(string.IsNullOrWhiteSpace(_sid)
                ? InteractiveUserIdentityResult.Unavailable()
                : InteractiveUserIdentityResult.Success(_sid));
        }
    }

    private sealed class MutableClock : ISystemClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "GaltekClassroom.Agent.BrowserDownloadPolicy.Tests",
                Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
