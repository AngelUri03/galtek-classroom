using System;
using System.Collections.Generic;
using System.Linq;

namespace GaltekClassroom.LegacyBundleCleanup
{
    public enum CleanupExitCode { Success = 0, Blocked = 2010, Failed = 2011, InvalidArguments = 2012, LogUnavailable = 2013 }
    public enum CleanupOutcome { Complete, NoOp, Blocked, Failed }
    public enum ProviderKind { DirectBundle, StableBundle, MsiPackage }
    public enum DependentClassification { SelfDependent, KnownLegacyDependent, CurrentDependent, UnknownDependent }
    public enum MutationKind { RemoveEdge, RemoveProvider, RemoveArp, RemoveCache }

    public sealed class LegacyBundleDefinition
    {
        public string BundleId { get; set; }
        public string Version { get; set; }
        public string ProviderKey { get; set; }
        public string CacheExecutableName { get; set; }
    }

    public sealed class LegacyProviderDefinition
    {
        public string Key { get; set; }
        public ProviderKind Kind { get; set; }
        public string OwnerId { get; set; }
        public string Version { get; set; }
        public string[] AllowedDependents { get; set; }
    }

    public sealed class CurrentBundleRecord
    {
        public string BundleId { get; set; }
        public string ProductName { get; set; }
        public string Manufacturer { get; set; }
        public string Version { get; set; }
        public string[] UpgradeCodes { get; set; }
        public string ProviderKey { get; set; }
        public string CachePath { get; set; }
        public bool CachePathSafe { get; set; }
        public int ProviderKeyRegistrationCount { get; set; }
        public int? ResumeMode { get; set; }
        public int? Installed { get; set; }
    }

    public sealed class LegacyBundleRecord
    {
        public string BundleId { get; set; }
        public bool ArpExists { get; set; }
        public string ProductName { get; set; }
        public string Manufacturer { get; set; }
        public string Version { get; set; }
        public string[] UpgradeCodes { get; set; }
        public string ProviderKey { get; set; }
        public string RegisteredCachePath { get; set; }
        public string ExpectedCachePath { get; set; }
        public bool CachePathSafe { get; set; }
        public bool CacheExists { get; set; }
    }

    public sealed class ProviderRecord
    {
        public string Key { get; set; }
        public bool Exists { get; set; }
        public string OwnerId { get; set; }
        public string Version { get; set; }
        public string DisplayName { get; set; }
        public string[] Dependents { get; set; }
    }

    public sealed class CleanupResult
    {
        public CleanupOutcome Outcome { get; set; }
        public string Reason { get; set; }
        public int Removed { get; set; }
    }

    public sealed class CleanupCheck
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
        public string Reason { get; set; }
        public string Expected { get; set; }
        public string Actual { get; set; }
    }

    public sealed class MutationStep
    {
        public MutationKind Kind { get; set; }
        public string ProviderKey { get; set; }
        public string DependentId { get; set; }
        public string BundleId { get; set; }
    }

    public interface ICleanupEnvironment
    {
        string CurrentBundleId { get; }
        CurrentBundleRecord GetCurrentBundle();
        ProviderRecord GetProvider(string providerKey);
        bool CurrentMsiInstalled { get; }
        bool ServiceHealthy { get; }
        bool SessionHealthy { get; }
        bool SnapshotPending { get; }
        LegacyBundleRecord GetBundle(LegacyBundleDefinition definition);
        bool IsMsiInstalled(string productCode);
        bool IsHistoricalBundleProcessActive(LegacyBundleDefinition definition);
        void RemoveDependencyEdge(string providerKey, string dependentId);
        void RemoveProvider(LegacyProviderDefinition definition);
        void RemoveCache(LegacyBundleDefinition definition);
        void RemoveArp(LegacyBundleDefinition definition);
    }

    public static class CleanupPolicy
    {
        public const string ProductName = "Galtek Classroom Client";
        public const string Manufacturer = "GaltekSolution";
        public const string UpgradeCode = "{A67E1418-0AD4-4DA1-915A-1098091D23E7}";
        public const string Bundle001 = "{2DCDF1F3-FBDD-42D6-8879-EA33126CA34C}";
        public const string Bundle002Broken = "{43F6BCC5-2BEE-4CCD-9B51-1A4BE0548D85}";
        public const string Bundle002C183 = "{C18369A6-9FC2-4360-9451-D77A44A7477C}";
        public const string Bundle003 = "{A2742DDC-6F6B-46F3-A42C-FF1FDFB8BD96}";
        public const string Msi001ProductCode = "{BB52C90B-304C-42DD-8E02-D4B3B1173BA6}";
        public const string Msi002ProductCode = "{DCF26CE0-A601-489B-B32A-E14095CC50C0}";
        public const string Msi003ProductCode = "{2BA4D6A0-9492-484E-A27B-9EAE40C886A9}";
        public const string Msi002ProviderKey = "{DCF26CE0-A601-489B-B32A-E14095CC50C0}_v0.0.2";
        public const string Msi003ProviderKey = "{2BA4D6A0-9492-484E-A27B-9EAE40C886A9}_v0.0.3";
        public const string Bundle002ProviderKey = "GaltekSolution.GaltekClassroom.Client.Bundle.0.0.2";
        public const string Bundle003ProviderKey = "GaltekSolution.GaltekClassroom.Client.Bundle.0.0.3";
        public const string CurrentVersion = "0.0.4";
        public const string CurrentMsiProductCode = "{A544ED43-3AAA-4B47-8EE7-0A3807C94D59}";
        public const string CurrentBundleProviderKey = "GaltekSolution.GaltekClassroom.Client.Bundle.0.0.4";
        public const string CurrentBundleExecutableName = "GaltekClassroom-Client-Setup-0.0.4.exe";
        public const int ResumeModeActive = 1;
        public const int ResumeModeArp = 3;

        public static readonly LegacyBundleDefinition[] Bundles =
        {
            Bundle(Bundle001, "0.0.1", Bundle001),
            Bundle(Bundle002Broken, "0.0.2", Bundle002Broken),
            Bundle(Bundle002C183, "0.0.2", Bundle002ProviderKey),
            Bundle(Bundle003, "0.0.3", Bundle003ProviderKey)
        };

        public static readonly LegacyProviderDefinition[] Providers =
        {
            Provider(Bundle001, ProviderKind.DirectBundle, Bundle001, "0.0.1", Bundle001),
            Provider(Bundle002Broken, ProviderKind.DirectBundle, Bundle002Broken, "0.0.2", Bundle002Broken),
            Provider(Bundle002ProviderKey, ProviderKind.StableBundle, Bundle002C183, "0.0.2", Bundle002C183),
            Provider(Bundle003ProviderKey, ProviderKind.StableBundle, Bundle003, "0.0.3", Bundle003),
            Provider(Msi002ProviderKey, ProviderKind.MsiPackage, Msi002ProductCode, "0.0.2", Bundle002Broken, Bundle002C183),
            Provider(Msi003ProviderKey, ProviderKind.MsiPackage, Msi003ProductCode, "0.0.3", Bundle003)
        };

        public static bool TryGetBundle(string id, out LegacyBundleDefinition definition)
        {
            definition = Bundles.FirstOrDefault(x => EqualsIgnoreCase(x.BundleId, id));
            return definition != null;
        }

        public static bool TryGetProvider(string key, out LegacyProviderDefinition definition)
        {
            definition = Providers.FirstOrDefault(x => String.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            return definition != null;
        }

        public static bool IsValidCurrentBundleId(string id) => Guid.TryParse(id, out _) && Bundles.All(x => !EqualsIgnoreCase(x.BundleId, id));

        public static bool IsKnownEdge(string providerKey, string dependentId) =>
            TryGetProvider(providerKey, out var provider) && provider.AllowedDependents.Any(x => EqualsIgnoreCase(x, dependentId));

        public static DependentClassification ClassifyDependent(LegacyProviderDefinition provider, string dependentId, string currentBundleId)
        {
            if (EqualsIgnoreCase(dependentId, currentBundleId)) return DependentClassification.CurrentDependent;
            if (provider.Kind == ProviderKind.DirectBundle && EqualsIgnoreCase(provider.Key, dependentId)) return DependentClassification.SelfDependent;
            if (provider.AllowedDependents.Any(x => EqualsIgnoreCase(x, dependentId))) return DependentClassification.KnownLegacyDependent;
            return DependentClassification.UnknownDependent;
        }

        public static IEnumerable<CleanupCheck> ValidateHost(ICleanupEnvironment environment, CurrentBundleRecord current, ProviderRecord currentProvider)
        {
            yield return Check("CURRENT_BUNDLE_AUTHORITY", IsValidCurrentBundleId(environment.CurrentBundleId), "CURRENT_BUNDLE_ID_INVALID", "GENERATED_0.0.4_ID", environment.CurrentBundleId);
            yield return Check("CURRENT_BUNDLE_REGISTRATION", current != null, "CURRENT_BUNDLE_REGISTRATION_MISSING", environment.CurrentBundleId, current?.BundleId ?? "MISSING");
            if (current != null)
            {
                yield return Check("CURRENT_BUNDLE_ID", EqualsIgnoreCase(current.BundleId, environment.CurrentBundleId), "CURRENT_BUNDLE_ID_MISMATCH", environment.CurrentBundleId, current.BundleId);
                yield return Check("CURRENT_PRODUCT", String.Equals(current.ProductName, ProductName, StringComparison.Ordinal), "CURRENT_PRODUCT_NAME_MISMATCH", ProductName, current.ProductName);
                yield return Check("CURRENT_PUBLISHER", String.Equals(current.Manufacturer, Manufacturer, StringComparison.Ordinal), "CURRENT_MANUFACTURER_MISMATCH", Manufacturer, current.Manufacturer);
                yield return Check("CURRENT_VERSION", NormalizeVersion(current.Version) == CurrentVersion, "CURRENT_VERSION_MISMATCH", CurrentVersion, current.Version);
                yield return Check("CURRENT_UPGRADE_CODE", ContainsIgnoreCase(current.UpgradeCodes, UpgradeCode), "CURRENT_UPGRADE_IDENTITY_MISMATCH", UpgradeCode, Join(current.UpgradeCodes));
                yield return Check("CURRENT_PROVIDER_KEY", String.Equals(current.ProviderKey, CurrentBundleProviderKey, StringComparison.Ordinal), "CURRENT_PROVIDER_KEY_MISMATCH", CurrentBundleProviderKey, current.ProviderKey);
                yield return Check("CURRENT_PROVIDER_UNIQUE", current.ProviderKeyRegistrationCount == 1, "CURRENT_PROVIDER_NOT_UNIQUE", "1", current.ProviderKeyRegistrationCount.ToString());
                yield return Check("CURRENT_CACHE_PATH", current.CachePathSafe, "CURRENT_CACHE_PATH_UNSAFE", "EXACT_CURRENT_CACHE", current.CachePathSafe ? "EXACT_CURRENT_CACHE" : "OTHER_OR_MISSING");
                var active = current.ResumeMode == ResumeModeActive && (current.Installed == 0 || current.Installed == 1);
                var finalized = current.ResumeMode == ResumeModeArp && current.Installed == 1;
                yield return Check("CURRENT_REGISTRATION_LIFECYCLE", active || finalized, "CURRENT_REGISTRATION_LIFECYCLE_INVALID", "ACTIVE_OR_FULL", Lifecycle(current));
            }
            yield return Check("CURRENT_PROVIDER", currentProvider != null && currentProvider.Exists, "CURRENT_PROVIDER_MISSING", CurrentBundleProviderKey, currentProvider?.Exists == true ? "PRESENT" : "ABSENT");
            if (currentProvider != null && currentProvider.Exists)
            {
                yield return Check("CURRENT_PROVIDER_OWNER", EqualsIgnoreCase(currentProvider.OwnerId, environment.CurrentBundleId), "CURRENT_PROVIDER_OWNERSHIP_UNSAFE", environment.CurrentBundleId, currentProvider.OwnerId);
                yield return Check("CURRENT_PROVIDER_VERSION", NormalizeVersion(currentProvider.Version) == CurrentVersion, "CURRENT_PROVIDER_VERSION_MISMATCH", CurrentVersion, currentProvider.Version);
                yield return Check("CURRENT_PROVIDER_NAME", String.Equals(currentProvider.DisplayName, ProductName, StringComparison.Ordinal), "CURRENT_PROVIDER_NAME_MISMATCH", ProductName, currentProvider.DisplayName);
            }
            yield return Check("CURRENT_MSI", environment.CurrentMsiInstalled, "CURRENT_MSI_MISSING", CurrentMsiProductCode, environment.CurrentMsiInstalled ? "PRESENT" : "ABSENT");
            yield return Check("OLD_MSI_001", !environment.IsMsiInstalled(Msi001ProductCode), "OLD_MSI_STILL_INSTALLED", "ABSENT", environment.IsMsiInstalled(Msi001ProductCode) ? "PRESENT" : "ABSENT");
            yield return Check("OLD_MSI_002", !environment.IsMsiInstalled(Msi002ProductCode), "OLD_MSI_STILL_INSTALLED", "ABSENT", environment.IsMsiInstalled(Msi002ProductCode) ? "PRESENT" : "ABSENT");
            yield return Check("OLD_MSI_003", !environment.IsMsiInstalled(Msi003ProductCode), "OLD_MSI_STILL_INSTALLED", "ABSENT", environment.IsMsiInstalled(Msi003ProductCode) ? "PRESENT" : "ABSENT");
            yield return Check("CURRENT_SERVICE", environment.ServiceHealthy, "CURRENT_SERVICE_UNHEALTHY", "RUNNING_AUTOMATIC_DELAYED_LOCALSYSTEM", environment.ServiceHealthy ? "HEALTHY" : "OTHER");
            yield return Check("CURRENT_SESSION", environment.SessionHealthy, "CURRENT_SESSION_UNHEALTHY", "ENABLED_READY_OR_RUNNING_EXACT_PATH", environment.SessionHealthy ? "HEALTHY" : "OTHER");
            yield return Check("ROLLBACK_SNAPSHOT", !environment.SnapshotPending, "ROLLBACK_SNAPSHOT_PENDING", "ABSENT", environment.SnapshotPending ? "PRESENT" : "ABSENT");
        }

        public static IEnumerable<CleanupCheck> ValidateBundle(LegacyBundleDefinition definition, LegacyBundleRecord bundle)
        {
            if (!bundle.ArpExists) yield break;
            yield return Check("LEGACY_BUNDLE_ID", EqualsIgnoreCase(bundle.BundleId, definition.BundleId), "BUNDLE_ID_MISMATCH", definition.BundleId, bundle.BundleId);
            yield return Check("LEGACY_PRODUCT", String.Equals(bundle.ProductName, ProductName, StringComparison.Ordinal), "PRODUCT_NAME_MISMATCH", ProductName, bundle.ProductName);
            yield return Check("LEGACY_PUBLISHER", String.Equals(bundle.Manufacturer, Manufacturer, StringComparison.Ordinal), "MANUFACTURER_MISMATCH", Manufacturer, bundle.Manufacturer);
            yield return Check("LEGACY_UPGRADE_CODE", ContainsIgnoreCase(bundle.UpgradeCodes, UpgradeCode), "UPGRADE_IDENTITY_MISMATCH", UpgradeCode, Join(bundle.UpgradeCodes));
            yield return Check("LEGACY_VERSION", NormalizeVersion(bundle.Version) == definition.Version, "VERSION_NOT_ALLOWED", definition.Version, bundle.Version);
            yield return Check("LEGACY_PROVIDER_KEY", String.Equals(bundle.ProviderKey, definition.ProviderKey, StringComparison.OrdinalIgnoreCase), "PROVIDER_KEY_MISMATCH", definition.ProviderKey, bundle.ProviderKey);
            yield return Check("LEGACY_CACHE_PATH", bundle.CachePathSafe, "CACHE_PATH_UNSAFE", "EXACT_OR_MISSING", bundle.CachePathSafe ? (bundle.CacheExists ? "EXACT_PRESENT" : "EXACT_MISSING") : "OTHER");
        }

        public static IEnumerable<CleanupCheck> ValidateProvider(LegacyProviderDefinition definition, ProviderRecord provider)
        {
            if (!provider.Exists) yield break;
            yield return Check("LEGACY_PROVIDER_OWNER", EqualsIgnoreCase(provider.OwnerId, definition.OwnerId), "PROVIDER_OWNERSHIP_UNSAFE", definition.OwnerId, provider.OwnerId);
            yield return Check("LEGACY_PROVIDER_VERSION", NormalizeVersion(provider.Version) == definition.Version, "PROVIDER_VERSION_MISMATCH", definition.Version, provider.Version);
            yield return Check("LEGACY_PROVIDER_NAME", String.Equals(provider.DisplayName, ProductName, StringComparison.Ordinal), "PROVIDER_NAME_MISMATCH", ProductName, provider.DisplayName);
        }

        internal static CleanupCheck Check(string name, bool passed, string reason, string expected, string actual) =>
            new CleanupCheck { Name = name, Passed = passed, Reason = reason, Expected = Safe(expected), Actual = Safe(actual) };
        internal static bool EqualsIgnoreCase(string left, string right) => String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        internal static string NormalizeVersion(string value) => Version.TryParse(value, out var parsed) ? parsed.ToString(3) : value;
        private static bool ContainsIgnoreCase(IEnumerable<string> values, string expected) => values != null && values.Any(x => EqualsIgnoreCase(x, expected));
        private static string Join(IEnumerable<string> values) => values == null ? "MISSING" : String.Join(",", values);
        private static string Lifecycle(CurrentBundleRecord value) => "resume=" + (value.ResumeMode?.ToString() ?? "MISSING") + ",installed=" + (value.Installed?.ToString() ?? "MISSING");
        private static string Safe(string value) => String.IsNullOrWhiteSpace(value) ? "MISSING" : value.Replace(' ', '_').Replace('\r', '_').Replace('\n', '_');
        private static LegacyBundleDefinition Bundle(string id, string version, string providerKey) => new LegacyBundleDefinition
        {
            BundleId = id, Version = version, ProviderKey = providerKey, CacheExecutableName = "GaltekClassroom-Client-Setup-" + version + ".exe"
        };
        private static LegacyProviderDefinition Provider(string key, ProviderKind kind, string owner, string version, params string[] dependents) =>
            new LegacyProviderDefinition { Key = key, Kind = kind, OwnerId = owner, Version = version, AllowedDependents = dependents };
    }

    public sealed class CleanupCoordinator
    {
        public CleanupResult Run(ICleanupEnvironment environment, IEnumerable<string> detectedPredecessors, Action<string> log = null)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            var supplied = (detectedPredecessors ?? Enumerable.Empty<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            var ids = supplied.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            log?.Invoke("CLEANUP_PRECONDITION name=PREDECESSOR_INPUT result=PASS expected=ALLOWLIST_ONLY actual=count_" + ids.Count + (supplied.Count == ids.Count ? String.Empty : "_duplicates_normalized"));
            foreach (var id in ids)
            {
                if (CleanupPolicy.TryGetBundle(id, out _)) continue;
                LogCheck(CleanupPolicy.Check("PREDECESSOR_ALLOWLIST", false, "BUNDLE_NOT_ALLOWLISTED", "KNOWN_IDS", id), log);
                return Block("BUNDLE_NOT_ALLOWLISTED", log);
            }

            log?.Invoke("CLEANUP_GRAPH_BEGIN bundles=4 providers=6");
            var bundles = CleanupPolicy.Bundles.Select(x => new { Definition = x, Record = environment.GetBundle(x) }).ToList();
            var providers = CleanupPolicy.Providers.Select(x => new { Definition = x, Record = environment.GetProvider(x.Key) }).ToList();
            var current = environment.GetCurrentBundle();
            var currentProvider = environment.GetProvider(CleanupPolicy.CurrentBundleProviderKey);

            foreach (var item in bundles)
                log?.Invoke("CLEANUP_GRAPH_NODE type=BUNDLE id=" + item.Definition.BundleId + " state=" + (item.Record.ArpExists ? "PRESENT" : "ABSENT") + " owner=" + (item.Record.ArpExists ? item.Record.ProviderKey : "NONE"));
            foreach (var item in providers)
            {
                log?.Invoke("CLEANUP_GRAPH_NODE type=PROVIDER id=" + item.Definition.Key + " state=" + (item.Record.Exists ? "PRESENT" : "ABSENT") + " owner=" + (item.Record.OwnerId ?? "NONE"));
                foreach (var dependent in item.Record.Dependents ?? new string[0])
                {
                    var classification = CleanupPolicy.ClassifyDependent(item.Definition, dependent, environment.CurrentBundleId);
                    log?.Invoke("CLEANUP_GRAPH_EDGE provider=" + item.Definition.Key + " dependent=" + dependent + " classification=" + ClassificationLog(classification));
                }
            }

            var hasResidue = bundles.Any(x => x.Record.ArpExists || x.Record.CacheExists) || providers.Any(x => x.Record.Exists);
            if (!hasResidue) return Complete(CleanupOutcome.NoOp, "ALREADY_CLEAN", 0, log);

            var checks = CleanupPolicy.ValidateHost(environment, current, currentProvider).ToList();
            foreach (var item in bundles)
            {
                checks.AddRange(CleanupPolicy.ValidateBundle(item.Definition, item.Record));
                var active = environment.IsHistoricalBundleProcessActive(item.Definition);
                checks.Add(CleanupPolicy.Check("HISTORICAL_PROCESS_" + item.Definition.BundleId, !active, "OLD_BUNDLE_PROCESS_ACTIVE", "ABSENT", active ? "ACTIVE" : "ABSENT"));
            }
            foreach (var item in providers)
            {
                checks.AddRange(CleanupPolicy.ValidateProvider(item.Definition, item.Record));
                foreach (var dependent in item.Record.Dependents ?? new string[0])
                {
                    var classification = CleanupPolicy.ClassifyDependent(item.Definition, dependent, environment.CurrentBundleId);
                    if (classification == DependentClassification.CurrentDependent)
                        checks.Add(CleanupPolicy.Check("DEPENDENT_CLASSIFICATION", false, "CURRENT_DEPENDENT", "HISTORICAL_ONLY", dependent));
                    else if (classification == DependentClassification.UnknownDependent)
                        checks.Add(CleanupPolicy.Check("DEPENDENT_CLASSIFICATION", false, "UNKNOWN_DEPENDENT", "EXACT_KNOWN_EDGE", dependent));
                }
            }

            foreach (var check in checks) LogCheck(check, log);
            var failed = checks.FirstOrDefault(x => !x.Passed);
            if (failed != null) return Block(failed.Reason, log);

            var plan = BuildPlan(bundles.Select(x => Tuple.Create(x.Definition, x.Record)), providers.Select(x => Tuple.Create(x.Definition, x.Record)));
            foreach (var step in plan) log?.Invoke("CLEANUP_PLAN_STEP kind=" + MutationLog(step.Kind) + " target=" + StepTarget(step));
            log?.Invoke("CLEANUP_GRAPH_VALIDATED planSteps=" + plan.Count);

            var removed = 0;
            try
            {
                foreach (var step in plan) { ExecuteStep(step, environment, log); removed++; }
                var remainingBundle = CleanupPolicy.Bundles.Any(x => { var value = environment.GetBundle(x); return value.ArpExists || value.CacheExists; });
                var remainingProvider = CleanupPolicy.Providers.Any(x => environment.GetProvider(x.Key).Exists);
                if (remainingBundle || remainingProvider) return Failed("HISTORICAL_GRAPH_STILL_PRESENT", removed, null, log);
                return Complete(CleanupOutcome.Complete, "CLEAN", removed, log);
            }
            catch (Exception ex) { return Failed("MUTATION_FAILED", removed, ex, log); }
        }

        private static List<MutationStep> BuildPlan(IEnumerable<Tuple<LegacyBundleDefinition, LegacyBundleRecord>> bundles, IEnumerable<Tuple<LegacyProviderDefinition, ProviderRecord>> providers)
        {
            var result = new List<MutationStep>();
            var providerList = providers.ToList();
            foreach (var item in providerList)
                foreach (var dependent in item.Item2.Dependents ?? new string[0])
                    result.Add(new MutationStep { Kind = MutationKind.RemoveEdge, ProviderKey = item.Item1.Key, DependentId = dependent });
            foreach (var item in providerList.Where(x => x.Item2.Exists))
                result.Add(new MutationStep { Kind = MutationKind.RemoveProvider, ProviderKey = item.Item1.Key });
            foreach (var item in bundles.Where(x => x.Item2.ArpExists))
                result.Add(new MutationStep { Kind = MutationKind.RemoveArp, BundleId = item.Item1.BundleId });
            foreach (var item in bundles.Where(x => x.Item2.CacheExists))
                result.Add(new MutationStep { Kind = MutationKind.RemoveCache, BundleId = item.Item1.BundleId });
            return result;
        }

        private static void ExecuteStep(MutationStep step, ICleanupEnvironment environment, Action<string> log)
        {
            var target = StepTarget(step);
            log?.Invoke("CLEANUP_STEP kind=" + MutationLog(step.Kind) + " target=" + target + " result=BEGIN");
            switch (step.Kind)
            {
                case MutationKind.RemoveEdge:
                    if (!CleanupPolicy.IsKnownEdge(step.ProviderKey, step.DependentId)) throw new InvalidOperationException("EDGE_IDENTITY_CHANGED");
                    environment.RemoveDependencyEdge(step.ProviderKey, step.DependentId);
                    break;
                case MutationKind.RemoveProvider:
                    if (!CleanupPolicy.TryGetProvider(step.ProviderKey, out var provider)) throw new InvalidOperationException("PROVIDER_NOT_ALLOWLISTED");
                    var live = environment.GetProvider(step.ProviderKey);
                    if (live.Exists && (live.Dependents?.Length ?? 0) != 0) throw new InvalidOperationException("PROVIDER_NOT_EMPTY");
                    environment.RemoveProvider(provider);
                    break;
                case MutationKind.RemoveArp:
                    if (!CleanupPolicy.TryGetBundle(step.BundleId, out var arp)) throw new InvalidOperationException("BUNDLE_NOT_ALLOWLISTED");
                    environment.RemoveArp(arp);
                    break;
                case MutationKind.RemoveCache:
                    if (!CleanupPolicy.TryGetBundle(step.BundleId, out var cache)) throw new InvalidOperationException("BUNDLE_NOT_ALLOWLISTED");
                    environment.RemoveCache(cache);
                    break;
                default: throw new InvalidOperationException("UNKNOWN_PLAN_STEP");
            }
            log?.Invoke("CLEANUP_STEP kind=" + MutationLog(step.Kind) + " target=" + target + " result=PASS");
        }

        private static string StepTarget(MutationStep step) => step.Kind == MutationKind.RemoveEdge ? step.ProviderKey + "->" + step.DependentId : step.ProviderKey ?? step.BundleId;
        private static string ClassificationLog(DependentClassification value)
        {
            switch (value)
            {
                case DependentClassification.SelfDependent: return "SELF_DEPENDENT";
                case DependentClassification.KnownLegacyDependent: return "KNOWN_LEGACY_DEPENDENT";
                case DependentClassification.CurrentDependent: return "CURRENT_DEPENDENT";
                default: return "UNKNOWN_DEPENDENT";
            }
        }
        private static string MutationLog(MutationKind value)
        {
            switch (value)
            {
                case MutationKind.RemoveEdge: return "REMOVE_EDGE";
                case MutationKind.RemoveProvider: return "REMOVE_PROVIDER";
                case MutationKind.RemoveArp: return "REMOVE_ARP";
                default: return "REMOVE_CACHE";
            }
        }
        private static void LogCheck(CleanupCheck check, Action<string> log) => log?.Invoke("CLEANUP_PRECONDITION name=" + check.Name + " result=" + (check.Passed ? "PASS" : "FAIL") + " expected=" + check.Expected + " actual=" + check.Actual);
        private static CleanupResult Block(string reason, Action<string> log) { log?.Invoke("CLEANUP_BLOCKED reason=" + reason); return new CleanupResult { Outcome = CleanupOutcome.Blocked, Reason = reason }; }
        private static CleanupResult Failed(string reason, int removed, Exception exception, Action<string> log) { log?.Invoke("CLEANUP_FAILED reason=" + reason + " exception=" + (exception?.GetType().Name ?? "NONE") + " removed=" + removed); return new CleanupResult { Outcome = CleanupOutcome.Failed, Reason = reason, Removed = removed }; }
        private static CleanupResult Complete(CleanupOutcome outcome, string reason, int removed, Action<string> log) { log?.Invoke("CLEANUP_COMPLETE outcome=" + outcome.ToString().ToUpperInvariant() + " reason=" + reason + " removed=" + removed); return new CleanupResult { Outcome = outcome, Reason = reason, Removed = removed }; }
    }
}
