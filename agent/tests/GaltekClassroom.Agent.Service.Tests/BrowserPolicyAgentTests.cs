using GaltekClassroom.Agent.Service.BrowserPolicy;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.OpenUrl;
using GaltekClassroom.Agent.Service.SessionCommands;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class BrowserPolicyAgentTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compiler_MapsHostExactDnsToNativeExactHost()
    {
        CompiledBrowserPolicy policy = Compile(Policy(BrowserPolicyMode.Blocklist,
            Rule("r1", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostExact, "Example.com")));

        Assert.Equal([".example.com"], policy.BlockFilters);
    }

    [Fact]
    public void Compiler_MapsHostSuffixToNativeSuffix()
    {
        CompiledBrowserPolicy policy = Compile(Policy(BrowserPolicyMode.Blocklist,
            Rule("r1", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostSuffix, "example.com")));

        Assert.Equal(["example.com"], policy.BlockFilters);
    }

    [Fact]
    public void Compiler_DoesNotAddDotForHostExactIp()
    {
        CompiledBrowserPolicy policy = Compile(Policy(BrowserPolicyMode.Blocklist,
            Rule("r1", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostExact, "192.168.1.10")));

        Assert.Equal(["192.168.1.10"], policy.BlockFilters);
    }

    [Fact]
    public void Compiler_UrlPrefixProducesExactDnsHostAndPreservesNonDefaultPort()
    {
        CompiledBrowserPolicy policy = Compile(Policy(BrowserPolicyMode.Blocklist,
            Rule("r1", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.UrlPrefix, "https://Example.com:8443/material/")));

        Assert.Equal(["https://.example.com:8443/material/"], policy.BlockFilters);
    }

    [Fact]
    public void Compiler_UrlPrefixNormalizesDefaultPort()
    {
        CompiledBrowserPolicy policy = Compile(Policy(BrowserPolicyMode.Blocklist,
            Rule("r1", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.UrlPrefix, "https://example.com:443/material/")));

        Assert.Equal(["https://.example.com/material/"], policy.BlockFilters);
    }

    [Fact]
    public void Compiler_ExactUrlIsNotNativeEnforceable()
    {
        ChromiumBrowserPolicyCompilerResult result = new ChromiumBrowserPolicyCompiler().Compile(Policy(BrowserPolicyMode.Blocklist,
            Rule("r1", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.ExactUrl, "https://example.com/path?q=1")));

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserPolicyNotNativeEnforceable, result.ErrorCode);
    }

    [Fact]
    public void Compiler_UnrestrictedProducesEmptyLists()
    {
        CompiledBrowserPolicy policy = Compile(Policy(BrowserPolicyMode.Unrestricted,
            Rule("r1", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostSuffix, "example.com")));

        Assert.Empty(policy.BlockFilters);
        Assert.Empty(policy.AllowFilters);
    }

    [Fact]
    public void Compiler_AllowlistIncludesWildcardBlockAndCompilesAllowAndBlockRules()
    {
        CompiledBrowserPolicy policy = Compile(Policy(BrowserPolicyMode.Allowlist,
            Rule("allow", BrowserPolicyRuleAction.Allow, BrowserPolicyRuleMatchType.HostSuffix, "school.test"),
            Rule("block", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.UrlPrefix, "https://school.test/chat/")));

        Assert.Contains("*", policy.BlockFilters);
        Assert.Contains("https://.school.test/chat/", policy.BlockFilters);
        Assert.Equal(["school.test"], policy.AllowFilters);
    }

    [Fact]
    public void Compiler_DedupesSortsAndHashesDeterministically()
    {
        var first = Compile(Policy(BrowserPolicyMode.Blocklist,
            Rule("b", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostSuffix, "z.test"),
            Rule("a", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostSuffix, "a.test"),
            Rule("a2", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostSuffix, "a.test")));
        var second = Compile(Policy(BrowserPolicyMode.Blocklist,
            Rule("a", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostSuffix, "a.test"),
            Rule("b", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostSuffix, "z.test")));

        Assert.Equal(["a.test", "z.test"], first.BlockFilters);
        Assert.Equal(first.ContentHash, second.ContentHash);
    }

    [Fact]
    public void Compiler_RejectsPolicyLargerThanNativeLimit()
    {
        var parameters = Policy(BrowserPolicyMode.Blocklist);
        for (int i = 0; i < 1001; i++)
        {
            parameters.Rules.Add(Rule($"r{i}", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostSuffix, $"site{i}.test"));
        }

        ChromiumBrowserPolicyCompilerResult result = new ChromiumBrowserPolicyCompiler().Compile(parameters);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserPolicyTooLarge, result.ErrorCode);
    }

    [Fact]
    public void Evaluator_UsesMostSpecificMatchBeforeAllowTieBreak()
    {
        var evaluator = new ChromiumBrowserPolicyEvaluator();
        var blockBroad = new CompiledBrowserPolicy("p", 1, BrowserPolicyMode.Blocklist, ["youtube.com"], ["https://.www.youtube.com/watch"], "h");
        var allowBroad = new CompiledBrowserPolicy("p", 1, BrowserPolicyMode.Blocklist, ["https://.www.youtube.com/watch"], ["youtube.com"], "h");
        var tie = new CompiledBrowserPolicy("p", 1, BrowserPolicyMode.Blocklist, ["school.test"], ["school.test"], "h");

        Assert.Equal(BrowserPolicyNavigationOutcome.Allow, evaluator.Evaluate(blockBroad, "https://www.youtube.com/watch?v=1").Outcome);
        Assert.Equal(BrowserPolicyNavigationOutcome.Block, evaluator.Evaluate(allowBroad, "https://www.youtube.com/watch?v=1").Outcome);
        Assert.Equal(BrowserPolicyNavigationOutcome.Allow, evaluator.Evaluate(tie, "https://www.school.test/").Outcome);
    }

    [Fact]
    public async Task ApplyService_FirstApplyToEmptyRegistryPersistsVerifiedState()
    {
        using var temp = new TempDirectory();
        var registry = new InMemoryRegistry();
        var service = CreateApplyService(registry, temp.Path);
        CompiledBrowserPolicy policy = Compile(Policy(BrowserPolicyMode.Allowlist,
            Rule("allow", BrowserPolicyRuleAction.Allow, BrowserPolicyRuleMatchType.HostSuffix, "school.test")));

        BrowserPolicyApplyResult result = await service.ApplyAsync("S-1-5-21-1", policy, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["*"], registry.Snapshot.ChromeBlock);
        Assert.Equal(["school.test"], registry.Snapshot.ChromeAllow);
        Assert.True(File.Exists(Path.Combine(temp.Path, BrowserNavigationPolicyStateStore.StateFileName)));
        Assert.False(File.Exists(Path.Combine(temp.Path, BrowserNavigationPolicyStateStore.JournalFileName)));
    }

    [Fact]
    public async Task ApplyService_FirstApplyWithUnknownExistingPolicyFailsConflict()
    {
        using var temp = new TempDirectory();
        var registry = new InMemoryRegistry { Snapshot = new BrowserRegistrySnapshot(["external.test"], [], [], []) };
        var service = CreateApplyService(registry, temp.Path);

        BrowserPolicyApplyResult result = await service.ApplyAsync("S-1-5-21-1", Compile(Policy(BrowserPolicyMode.Unrestricted)), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserPolicyExternalConflict, result.ErrorCode);
    }

    [Fact]
    public async Task ApplyService_MachinePolicyFailsConflict()
    {
        using var temp = new TempDirectory();
        var registry = new InMemoryRegistry { HasMachinePolicy = true };
        var service = CreateApplyService(registry, temp.Path);

        BrowserPolicyApplyResult result = await service.ApplyAsync("S-1-5-21-1", Compile(Policy(BrowserPolicyMode.Unrestricted)), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserPolicyExternalConflict, result.ErrorCode);
    }

    [Fact]
    public async Task ApplyService_SameHashDoesNotRewriteRegistry()
    {
        using var temp = new TempDirectory();
        var registry = new InMemoryRegistry();
        var service = CreateApplyService(registry, temp.Path);
        CompiledBrowserPolicy policy = Compile(Policy(BrowserPolicyMode.Blocklist,
            Rule("block", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostSuffix, "games.test")));

        Assert.True((await service.ApplyAsync("S-1-5-21-1", policy, CancellationToken.None)).Succeeded);
        BrowserPolicyApplyResult second = await service.ApplyAsync("S-1-5-21-1", policy, CancellationToken.None);

        Assert.True(second.Succeeded);
        Assert.True(second.NoChange);
        Assert.Equal(1, registry.ApplyCalls);
    }

    [Fact]
    public async Task ApplyService_ApplyFailureMapsStructuredRollbackStatus()
    {
        using var temp = new TempDirectory();
        var registry = new InMemoryRegistry { FailApply = true };
        var service = CreateApplyService(registry, temp.Path);

        BrowserPolicyApplyResult result = await service.ApplyAsync("S-1-5-21-1", Compile(Policy(BrowserPolicyMode.Blocklist,
            Rule("block", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostSuffix, "games.test"))), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.BrowserPolicyApplyFailed, result.ErrorCode);
    }

    [Fact]
    public async Task Handler_RejectsPrimaryOrSecondaryScopeWithoutSidBinding()
    {
        using var temp = new TempDirectory();
        var handler = new ApplyBrowserPolicyOperationHandler(
            new ChromiumBrowserPolicyCompiler(),
            new StaticUserResolver("S-1-5-21-1"),
            CreateApplyService(new InMemoryRegistry(), temp.Path),
            NullLogger<ApplyBrowserPolicyOperationHandler>.Instance);
        OperationRequest request = ApplyRequest(Policy(BrowserPolicyMode.Blocklist));
        request.ApplyBrowserPolicy.AccountScope = BrowserPolicyAccountScope.Primary;

        RemoteOperationHandlerResult result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.BrowserAccountScopeUnresolved, result.ErrorCode);
    }

    [Fact]
    public async Task OpenUrl_WhenAppliedPolicyBlocks_DoesNotCallSessionAgent()
    {
        using var temp = new TempDirectory();
        var registry = new InMemoryRegistry();
        var applyService = CreateApplyService(registry, temp.Path);
        await applyService.ApplyAsync("S-1-5-21-1", Compile(Policy(BrowserPolicyMode.Blocklist,
            Rule("block", BrowserPolicyRuleAction.Block, BrowserPolicyRuleMatchType.HostSuffix, "blocked.test"))), CancellationToken.None);
        var session = RecordingSessionCommandClient.Success();
        var handler = new OpenUrlOperationHandler(
            session,
            new OpenUrlSafetyPolicy(),
            NullLogger<OpenUrlOperationHandler>.Instance,
            new AppliedBrowserPolicyEvaluator(new StaticUserResolver("S-1-5-21-1"), applyService, new ChromiumBrowserPolicyEvaluator()));

        RemoteOperationHandlerResult result = await handler.HandleAsync(
            OpenUrlRequest("https://www.blocked.test/lesson"),
            CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.UrlBlockedByPolicy, result.ErrorCode);
        Assert.Empty(session.OpenUrlCalls);
    }

    [Fact]
    public void BrowserPolicyHandlers_DoNotCreateHostedTimer()
    {
        Assert.False(typeof(IHostedService).IsAssignableFrom(typeof(ApplyBrowserPolicyOperationHandler)));
        Assert.False(typeof(IHostedService).IsAssignableFrom(typeof(BrowserNavigationPolicyApplyService)));
        Assert.False(typeof(IHostedService).IsAssignableFrom(typeof(ChromiumBrowserPolicyCompiler)));
    }

    private static BrowserNavigationPolicyApplyService CreateApplyService(InMemoryRegistry registry, string dataDirectory)
    {
        return new BrowserNavigationPolicyApplyService(
            registry,
            new BrowserNavigationPolicyStateStore(dataDirectory),
            new MutableClock(FixedNow));
    }

    private static CompiledBrowserPolicy Compile(ApplyBrowserPolicyOperationParameters parameters)
    {
        ChromiumBrowserPolicyCompilerResult result = new ChromiumBrowserPolicyCompiler().Compile(parameters);
        Assert.True(result.Succeeded, result.Message);
        return result.Policy!;
    }

    private static ApplyBrowserPolicyOperationParameters Policy(
        BrowserPolicyMode mode,
        params BrowserPolicyRuleParameters[] rules)
    {
        var policy = new ApplyBrowserPolicyOperationParameters
        {
            PolicyId = "policy-1",
            PolicyVersion = 1,
            Mode = mode,
            AccountScope = BrowserPolicyAccountScope.Any
        };
        policy.Rules.AddRange(rules);
        return policy;
    }

    private static BrowserPolicyRuleParameters Rule(
        string id,
        BrowserPolicyRuleAction action,
        BrowserPolicyRuleMatchType matchType,
        string pattern)
    {
        return new BrowserPolicyRuleParameters
        {
            RuleId = id,
            Action = action,
            MatchType = matchType,
            Pattern = pattern,
            Enabled = true
        };
    }

    private static OperationRequest ApplyRequest(ApplyBrowserPolicyOperationParameters parameters)
    {
        return new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.ApplyBrowserNavigationPolicy,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            ApplyBrowserPolicy = parameters
        };
    }

    private static OperationRequest OpenUrlRequest(string url)
    {
        return new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.OpenUrl,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            OpenUrl = new OpenUrlOperationParameters { Url = url }
        };
    }

    private sealed class InMemoryRegistry : IBrowserPolicyRegistryStore
    {
        public BrowserRegistrySnapshot Snapshot { get; set; } = BrowserRegistrySnapshot.Empty;

        public bool HasMachinePolicy { get; set; }

        public bool FailApply { get; set; }

        public int ApplyCalls { get; private set; }

        public Task<BrowserRegistrySnapshot> ReadUserPolicyAsync(string windowsSid, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot);
        }

        public Task<bool> HasMachineLevelPolicyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(HasMachinePolicy);
        }

        public Task<BrowserPolicyRegistryApplyResult> ApplyUserPolicyAsync(
            string windowsSid,
            BrowserRegistrySnapshot desired,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailApply)
            {
                return Task.FromResult(BrowserPolicyRegistryApplyResult.Failure(
                    rollbackSucceeded: true,
                    NetworkOperationErrorCode.BrowserPolicyApplyFailed,
                    "Apply failed."));
            }

            ApplyCalls++;
            Snapshot = desired;
            return Task.FromResult(BrowserPolicyRegistryApplyResult.Success());
        }
    }

    private sealed class StaticUserResolver : IInteractiveUserIdentityResolver
    {
        private readonly string? _sid;

        public StaticUserResolver(string? sid)
        {
            _sid = sid;
        }

        public Task<InteractiveUserIdentityResult> ResolveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(string.IsNullOrWhiteSpace(_sid)
                ? InteractiveUserIdentityResult.Unavailable()
                : InteractiveUserIdentityResult.Success(_sid));
        }
    }

    private sealed class RecordingSessionCommandClient : ISessionCommandClient
    {
        public List<(string OperationId, string Url)> OpenUrlCalls { get; } = [];

        public static RecordingSessionCommandClient Success()
        {
            return new RecordingSessionCommandClient();
        }

        public Task<SessionCommandClientResult> OpenUrlAsync(
            string operationId,
            string url,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenUrlCalls.Add((operationId, url));
            return Task.FromResult(SessionCommandClientResult.Success());
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
                "GaltekClassroom.Agent.BrowserPolicy.Tests",
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
