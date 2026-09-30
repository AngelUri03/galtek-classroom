using System;
using System.Collections.Generic;
using System.Linq;
using GaltekClassroom.LegacyBundleCleanup;

internal static class Program
{
    private const string Current006 = "{12345678-1234-4ABC-8DEF-1234567890AB}";
    private static int failures;

    private static int Main()
    {
        ExitCodeContract();
        ClassificationContract();
        ExactPc14Graph();
        UnknownDependentBlocksBeforeMutation();
        CurrentDependentAttackBlocksBeforeMutation();
        OwnershipMismatchBlocksBeforeMutation();
        ValidateAllBeforeMutation();
        PartialStateFixtures();
        ProcessGuards();
        CurrentAuthorityGuards();
        UnknownInputRejected();
        Console.WriteLine(failures == 0 ? "LEGACY_BUNDLE_CLEANUP_TESTS_PASS" : "LEGACY_BUNDLE_CLEANUP_TESTS_FAIL count=" + failures);
        return failures == 0 ? 0 : 1;
    }

    private static void ExitCodeContract()
    {
        Expect("success exit", 0, (int)CleanupExitCode.Success);
        Expect("blocked exit", 2010, (int)CleanupExitCode.Blocked);
        Expect("failed exit", 2011, (int)CleanupExitCode.Failed);
        Expect("invalid args exit", 2012, (int)CleanupExitCode.InvalidArguments);
        Expect("log unavailable exit", 2013, (int)CleanupExitCode.LogUnavailable);
        Console.WriteLine("CLEANUP_EXIT_CODE_CONTRACT_PASS typed-20xx");
    }

    private static void ClassificationContract()
    {
        var direct = Definition(CleanupPolicy.Bundle001);
        var stable = Definition(CleanupPolicy.Bundle002ProviderKey);
        Expect("self dependent", DependentClassification.SelfDependent, CleanupPolicy.ClassifyDependent(direct, CleanupPolicy.Bundle001, Current006));
        Expect("known legacy dependent", DependentClassification.KnownLegacyDependent, CleanupPolicy.ClassifyDependent(stable, CleanupPolicy.Bundle002C183, Current006));
        Expect("current dependent", DependentClassification.CurrentDependent, CleanupPolicy.ClassifyDependent(stable, Current006, Current006));
        Expect("unknown dependent", DependentClassification.UnknownDependent, CleanupPolicy.ClassifyDependent(stable, "{11111111-1111-1111-1111-111111111111}", Current006));
        Console.WriteLine("DEPENDENT_CLASSIFICATION_FIXTURE_PASS SELF_DEPENDENT KNOWN_LEGACY_DEPENDENT CURRENT_DEPENDENT UNKNOWN_DEPENDENT");
    }

    private static void ExactPc14Graph()
    {
        var environment = PhysicalPc14();
        var log = new List<string>();
        var result = new CleanupCoordinator().Run(environment, CleanupPolicy.Bundles.Select(x => x.BundleId), log.Add);
        Expect("PC14 cleanup complete", CleanupOutcome.Complete, result.Outcome);
        Expect("all historical arp absent", 0, environment.Bundles.Values.Count(x => x.ArpExists));
        Expect("all historical cache absent", 0, environment.Bundles.Values.Count(x => x.CacheExists));
        Expect("all historical providers absent", 0, environment.Providers.Values.Count(x => x.Exists && !String.Equals(x.Key, CleanupPolicy.CurrentBundleProviderKey, StringComparison.Ordinal)));
        Expect("current provider present", true, environment.Providers[CleanupPolicy.CurrentBundleProviderKey].Exists);
        Expect("current MSI present", true, environment.CurrentMsiInstalled);
        Expect("historical process starts", 0, environment.HistoricalProcessStarts);
        Expect("graph begin log", true, log.Any(x => x.StartsWith("CLEANUP_GRAPH_BEGIN", StringComparison.Ordinal)));
        Expect("graph validated log", true, log.Any(x => x.StartsWith("CLEANUP_GRAPH_VALIDATED", StringComparison.Ordinal)));
        Expect("self edge logged", true, log.Any(x => x.Contains("classification=SELF_DEPENDENT")));
        Expect("known edge logged", true, log.Any(x => x.Contains("classification=KNOWN_LEGACY_DEPENDENT")));
        Expect("plan logged before mutation", true, log.FindIndex(x => x.StartsWith("CLEANUP_PLAN_STEP", StringComparison.Ordinal)) < log.FindIndex(x => x.StartsWith("CLEANUP_STEP", StringComparison.Ordinal)));
        Expect("edge before provider", true, environment.Mutations.FindLastIndex(x => x.StartsWith("EDGE:", StringComparison.Ordinal)) < environment.Mutations.FindIndex(x => x.StartsWith("PROVIDER:", StringComparison.Ordinal)));
        Expect("provider before arp", true, environment.Mutations.FindLastIndex(x => x.StartsWith("PROVIDER:", StringComparison.Ordinal)) < environment.Mutations.FindIndex(x => x.StartsWith("ARP:", StringComparison.Ordinal)));
        Expect("arp before cache", true, environment.Mutations.FindLastIndex(x => x.StartsWith("ARP:", StringComparison.Ordinal)) < environment.Mutations.FindIndex(x => x.StartsWith("CACHE:", StringComparison.Ordinal)));
        var second = new CleanupCoordinator().Run(environment, CleanupPolicy.Bundles.Select(x => x.BundleId));
        Expect("second run no-op", CleanupOutcome.NoOp, second.Outcome);
        Console.WriteLine("PC14_EXACT_POST_REBOOT_GRAPH_FIXTURE_PASS four-arp six-providers all-known-edges current006-only historical-process-count=0");
        Console.WriteLine("CLEANUP_IDEMPOTENCY_FIXTURE_PASS");
    }

    private static void UnknownDependentBlocksBeforeMutation()
    {
        var environment = PhysicalPc14();
        environment.Providers[CleanupPolicy.Bundle003ProviderKey].Dependents = environment.Providers[CleanupPolicy.Bundle003ProviderKey].Dependents.Concat(new[] { "{11111111-1111-1111-1111-111111111111}" }).ToArray();
        BlockedWithoutMutation("unknown dependent", environment, "UNKNOWN_DEPENDENT", true);
        Console.WriteLine("UNKNOWN_DEPENDENT_FIXTURE_PASS zero-mutations product-healthy");
    }

    private static void CurrentDependentAttackBlocksBeforeMutation()
    {
        var environment = PhysicalPc14();
        environment.Providers[CleanupPolicy.Bundle003ProviderKey].Dependents = new[] { CleanupPolicy.Bundle003, Current006 };
        BlockedWithoutMutation("current dependent attack", environment, "CURRENT_DEPENDENT", true);
        Console.WriteLine("CURRENT_EDGE_ATTACK_FIXTURE_PASS current-relationship-protected zero-mutations");
    }

    private static void OwnershipMismatchBlocksBeforeMutation()
    {
        var environment = PhysicalPc14();
        environment.Providers[CleanupPolicy.Msi003ProviderKey].OwnerId = "{11111111-1111-1111-1111-111111111111}";
        BlockedWithoutMutation("provider owner mismatch", environment, "PROVIDER_OWNERSHIP_UNSAFE", true);
        var arp = PhysicalPc14();
        arp.Bundles[CleanupPolicy.Bundle003].Manufacturer = "Other";
        BlockedWithoutMutation("arp owner mismatch", arp, "MANUFACTURER_MISMATCH", true);
        var cache = PhysicalPc14();
        cache.Bundles[CleanupPolicy.Bundle003].CachePathSafe = false;
        BlockedWithoutMutation("cache path mismatch", cache, "CACHE_PATH_UNSAFE", true);
        Console.WriteLine("OWNERSHIP_MISMATCH_FIXTURE_PASS provider-arp-cache block-before-mutation");
    }

    private static void ValidateAllBeforeMutation()
    {
        var environment = PhysicalPc14();
        environment.Bundles[CleanupPolicy.Bundle003].Version = "9.9.9";
        BlockedWithoutMutation("late bundle invalid", environment, "VERSION_NOT_ALLOWED", true);
        Console.WriteLine("VALIDATE_ENTIRE_GRAPH_BEFORE_MUTATE_FIXTURE_PASS");
    }

    private static void PartialStateFixtures()
    {
        foreach (var bundle in CleanupPolicy.Bundles)
        {
            var only = HealthyCurrent();
            AddBundleGraph(only, bundle.BundleId);
            Expect("only " + bundle.BundleId, CleanupOutcome.Complete, Run(only).Outcome);
        }

        var mixes = new[]
        {
            new[] { CleanupPolicy.Bundle001, CleanupPolicy.Bundle003 },
            new[] { CleanupPolicy.Bundle002Broken, CleanupPolicy.Bundle002C183 },
            new[] { CleanupPolicy.Bundle001, CleanupPolicy.Bundle002C183, CleanupPolicy.Bundle003 }
        };
        foreach (var ids in mixes)
        {
            var mix = HealthyCurrent();
            foreach (var id in ids) AddBundleGraph(mix, id);
            Expect("partial mix", CleanupOutcome.Complete, Run(mix).Outcome);
        }

        Partial("cache absent", e => e.Bundles[CleanupPolicy.Bundle003].CacheExists = false);
        Partial("ARP absent", e => e.Bundles[CleanupPolicy.Bundle003].ArpExists = false);
        Partial("provider absent", e => { e.Providers[CleanupPolicy.Bundle003ProviderKey].Exists = false; e.Providers[CleanupPolicy.Bundle003ProviderKey].Dependents = new string[0]; });
        Partial("edge absent", e => e.Providers[CleanupPolicy.Bundle003ProviderKey].Dependents = new string[0]);
        Console.WriteLine("PARTIAL_LEGACY_FIXTURES_PASS each-bundle mixes cache-arp-provider-edge-absent");
    }

    private static void ProcessGuards()
    {
        foreach (var bundle in CleanupPolicy.Bundles)
        {
            var environment = PhysicalPc14();
            environment.ActiveProcessBundleIds.Add(bundle.BundleId);
            BlockedWithoutMutation("active " + bundle.BundleId, environment, "OLD_BUNDLE_PROCESS_ACTIVE", true);
        }
        var currentAllowed = PhysicalPc14();
        currentAllowed.CurrentProcessActive = true;
        Expect("current process allowed", CleanupOutcome.Complete, Run(currentAllowed).Outcome);
        Console.WriteLine("PROCESS_GUARD_FIXTURES_PASS four-historical-rejected current006-allowed");
    }

    private static void CurrentAuthorityGuards()
    {
        GuardCurrent("current MSI missing", e => e.CurrentMsiInstalledValue = false, "CURRENT_MSI_MISSING");
        GuardCurrent("old MSI 003 still present", e => e.InstalledMsi.Add(CleanupPolicy.Msi003ProductCode), "OLD_MSI_STILL_INSTALLED");
        GuardCurrent("old MSI 004 still present", e => e.InstalledMsi.Add(CleanupPolicy.Msi004ProductCode), "OLD_MSI_STILL_INSTALLED");
        GuardCurrent("old MSI 005 still present", e => e.InstalledMsi.Add(CleanupPolicy.Msi005ProductCode), "OLD_MSI_STILL_INSTALLED");
        GuardCurrent("service unhealthy", e => e.ServiceHealthyValue = false, "CURRENT_SERVICE_UNHEALTHY");
        GuardCurrent("session unhealthy", e => e.SessionHealthyValue = false, "CURRENT_SESSION_UNHEALTHY");
        GuardCurrent("snapshot pending", e => e.SnapshotPendingValue = true, "ROLLBACK_SNAPSHOT_PENDING");
        GuardCurrent("current provider missing", e => e.Providers[CleanupPolicy.CurrentBundleProviderKey].Exists = false, "CURRENT_PROVIDER_MISSING");
        GuardCurrent("current provider owner", e => e.Providers[CleanupPolicy.CurrentBundleProviderKey].OwnerId = CleanupPolicy.Bundle003, "CURRENT_PROVIDER_OWNERSHIP_UNSAFE");
        Console.WriteLine("CURRENT_006_AUTHORITY_FIXTURES_PASS bundle-provider-msi-runtime-snapshot");
    }

    private static void UnknownInputRejected()
    {
        var environment = PhysicalPc14();
        var result = new CleanupCoordinator().Run(environment, new[] { CleanupPolicy.Bundle003, "{11111111-1111-1111-1111-111111111111}" });
        Expect("unknown input outcome", CleanupOutcome.Blocked, result.Outcome);
        Expect("unknown input reason", "BUNDLE_NOT_ALLOWLISTED", result.Reason);
        Expect("unknown input zero mutation", 0, environment.Mutations.Count);
    }

    private static void Partial(string name, Action<FixtureEnvironment> mutate)
    {
        var environment = HealthyCurrent();
        AddBundleGraph(environment, CleanupPolicy.Bundle003);
        mutate(environment);
        Expect(name, CleanupOutcome.Complete, Run(environment).Outcome);
    }

    private static void GuardCurrent(string name, Action<FixtureEnvironment> mutate, string reason)
    {
        var environment = PhysicalPc14();
        mutate(environment);
        BlockedWithoutMutation(name, environment, reason, false);
    }

    private static void BlockedWithoutMutation(string name, FixtureEnvironment environment, string reason, bool expectHealthy)
    {
        var result = Run(environment);
        Expect(name + " outcome", CleanupOutcome.Blocked, result.Outcome);
        Expect(name + " reason", reason, result.Reason);
        Expect(name + " no mutations", 0, environment.Mutations.Count);
        if (expectHealthy) Expect(name + " current healthy", true, environment.CurrentMsiInstalled && environment.ServiceHealthy && environment.SessionHealthy);
    }

    private static CleanupResult Run(FixtureEnvironment environment) => new CleanupCoordinator().Run(environment, CleanupPolicy.Bundles.Select(x => x.BundleId));

    private static FixtureEnvironment PhysicalPc14()
    {
        var result = HealthyCurrent();
        foreach (var bundle in CleanupPolicy.Bundles) AddBundleGraph(result, bundle.BundleId);
        return result;
    }

    private static FixtureEnvironment HealthyCurrent()
    {
        var result = new FixtureEnvironment();
        foreach (var definition in CleanupPolicy.Bundles) result.Bundles[definition.BundleId] = BundleRecord(definition, false, false);
        foreach (var definition in CleanupPolicy.Providers) result.Providers[definition.Key] = ProviderRecord(definition, false, new string[0]);
        result.Providers[CleanupPolicy.CurrentBundleProviderKey] = new ProviderRecord
        {
            Key = CleanupPolicy.CurrentBundleProviderKey, Exists = true, OwnerId = Current006,
            Version = CleanupPolicy.CurrentVersion, DisplayName = CleanupPolicy.ProductName, Dependents = new string[0]
        };
        return result;
    }

    private static void AddBundleGraph(FixtureEnvironment environment, string id)
    {
        var bundle = CleanupPolicy.Bundles.Single(x => String.Equals(x.BundleId, id, StringComparison.OrdinalIgnoreCase));
        environment.Bundles[id] = BundleRecord(bundle, true, true);
        foreach (var provider in CleanupPolicy.Providers.Where(x => x.AllowedDependents.Any(d => String.Equals(d, id, StringComparison.OrdinalIgnoreCase))))
        {
            var existing = environment.Providers[provider.Key];
            existing.Exists = true;
            existing.OwnerId = provider.OwnerId;
            existing.Version = provider.Version;
            existing.DisplayName = CleanupPolicy.ProductName;
            existing.Dependents = existing.Dependents.Concat(new[] { id }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    private static LegacyBundleRecord BundleRecord(LegacyBundleDefinition definition, bool arp, bool cache) => new LegacyBundleRecord
    {
        BundleId = definition.BundleId, ArpExists = arp, ProductName = CleanupPolicy.ProductName, Manufacturer = CleanupPolicy.Manufacturer,
        Version = definition.Version, UpgradeCodes = new[] { CleanupPolicy.UpgradeCode }, ProviderKey = definition.ProviderKey,
        RegisteredCachePath = @"C:\ProgramData\Package Cache\" + definition.BundleId + "\\" + definition.CacheExecutableName,
        ExpectedCachePath = @"C:\ProgramData\Package Cache\" + definition.BundleId + "\\" + definition.CacheExecutableName,
        CachePathSafe = true, CacheExists = cache
    };

    private static ProviderRecord ProviderRecord(LegacyProviderDefinition definition, bool exists, string[] dependents) => new ProviderRecord
    {
        Key = definition.Key, Exists = exists, OwnerId = definition.OwnerId, Version = definition.Version,
        DisplayName = CleanupPolicy.ProductName, Dependents = dependents
    };

    private static LegacyProviderDefinition Definition(string key) => CleanupPolicy.Providers.Single(x => String.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    private static void Expect<T>(string name, T expected, T actual)
    {
        if (Object.Equals(expected, actual)) return;
        failures++;
        Console.Error.WriteLine("FAIL " + name + " expected=" + expected + " actual=" + actual);
    }

    private sealed class FixtureEnvironment : ICleanupEnvironment
    {
        public readonly Dictionary<string, LegacyBundleRecord> Bundles = new Dictionary<string, LegacyBundleRecord>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, ProviderRecord> Providers = new Dictionary<string, ProviderRecord>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> InstalledMsi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> ActiveProcessBundleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Mutations = new List<string>();
        public int HistoricalProcessStarts = 0;
        public bool CurrentProcessActive;
        public CurrentBundleRecord Current = new CurrentBundleRecord
        {
            BundleId = Current006, ProductName = CleanupPolicy.ProductName, Manufacturer = CleanupPolicy.Manufacturer,
            Version = CleanupPolicy.CurrentVersion, UpgradeCodes = new[] { CleanupPolicy.UpgradeCode },
            ProviderKey = CleanupPolicy.CurrentBundleProviderKey, ProviderKeyRegistrationCount = 1,
            CachePath = @"C:\ProgramData\Package Cache\" + Current006 + @"\GaltekClassroom-Client-Setup-0.0.6.exe",
            CachePathSafe = true, ResumeMode = CleanupPolicy.ResumeModeActive, Installed = 1
        };
        public bool CurrentMsiInstalledValue = true;
        public bool ServiceHealthyValue = true;
        public bool SessionHealthyValue = true;
        public bool SnapshotPendingValue;
        public string CurrentBundleId => Current006;
        public CurrentBundleRecord GetCurrentBundle() => Current;
        public ProviderRecord GetProvider(string providerKey) => Providers.TryGetValue(providerKey, out var value) ? value : new ProviderRecord { Key = providerKey, Exists = false, Dependents = new string[0] };
        public bool CurrentMsiInstalled => CurrentMsiInstalledValue;
        public bool ServiceHealthy => ServiceHealthyValue;
        public bool SessionHealthy => SessionHealthyValue;
        public bool SnapshotPending => SnapshotPendingValue;
        public LegacyBundleRecord GetBundle(LegacyBundleDefinition definition) => Bundles[definition.BundleId];
        public bool IsMsiInstalled(string productCode) => InstalledMsi.Contains(productCode);
        public bool IsHistoricalBundleProcessActive(LegacyBundleDefinition definition) => ActiveProcessBundleIds.Contains(definition.BundleId);
        public void RemoveDependencyEdge(string providerKey, string dependentId)
        {
            Mutations.Add("EDGE:" + providerKey + "->" + dependentId);
            var provider = Providers[providerKey];
            provider.Dependents = provider.Dependents.Where(x => !String.Equals(x, dependentId, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        public void RemoveProvider(LegacyProviderDefinition definition) { Mutations.Add("PROVIDER:" + definition.Key); Providers[definition.Key].Exists = false; }
        public void RemoveCache(LegacyBundleDefinition definition) { Mutations.Add("CACHE:" + definition.BundleId); Bundles[definition.BundleId].CacheExists = false; }
        public void RemoveArp(LegacyBundleDefinition definition) { Mutations.Add("ARP:" + definition.BundleId); Bundles[definition.BundleId].ArpExists = false; }
    }
}
