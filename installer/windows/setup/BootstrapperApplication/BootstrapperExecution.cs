using System;
using System.Collections.Generic;
using WixToolset.BootstrapperApplicationApi;

namespace GaltekClassroom.Bootstrapper
{
    public enum BootstrapperMode
    {
        Interactive,
        Embedded,
        Silent
    }

    public sealed class BootstrapperExecutionPlan
    {
        public BootstrapperMode Mode { get; set; }
        public LaunchAction Action { get; set; }
        public Display Display { get; set; }
        public RelationType Relation { get; set; }
        public bool CreateWindow { get; set; }
        public bool InvokeInteractiveProductResolver { get; set; }
        public bool DetectOnRun { get; set; }
        public bool PlanCommandActionAfterDetect { get; set; }
        public bool ApplyAfterPlan { get; set; }
        public bool ShutdownAfterApply { get; set; }
    }

    public static class BootstrapperExecutionPolicy
    {
        public static BootstrapperExecutionPlan Resolve(LaunchAction action, Display display, RelationType relation)
        {
            var embedded = display == Display.Embedded || relation != RelationType.None;
            var interactive = display == Display.Full && relation == RelationType.None;
            return new BootstrapperExecutionPlan
            {
                Mode = embedded ? BootstrapperMode.Embedded : interactive ? BootstrapperMode.Interactive : BootstrapperMode.Silent,
                Action = action,
                Display = display,
                Relation = relation,
                CreateWindow = interactive,
                InvokeInteractiveProductResolver = interactive,
                DetectOnRun = true,
                PlanCommandActionAfterDetect = !interactive && action != LaunchAction.Unknown,
                ApplyAfterPlan = !interactive && action != LaunchAction.Unknown,
                ShutdownAfterApply = !interactive && action != LaunchAction.Unknown
            };
        }
    }

    public sealed class RelatedBundleObservation
    {
        public string BundleId { get; set; }
        public string Version { get; set; }
        public RelationType Relation { get; set; }
    }

    public sealed class LegacyBundleDisposition
    {
        public string Version { get; set; }
        public string Reason { get; set; }
    }

    public static class LegacyBrokenBundlePolicy
    {
        public const string Validated001BundleId = "{2DCDF1F3-FBDD-42D6-8879-EA33126CA34C}";
        public const string Failed002BundleId = "{43F6BCC5-2BEE-4CCD-9B51-1A4BE0548D85}";
        public const string C183002BundleId = "{C18369A6-9FC2-4360-9451-D77A44A7477C}";
        public const string Failed003BundleId = "{A2742DDC-6F6B-46F3-A42C-FF1FDFB8BD96}";

        private static readonly IDictionary<string, LegacyBundleDisposition> Unsafe =
            new Dictionary<string, LegacyBundleDisposition>(StringComparer.OrdinalIgnoreCase)
            {
                { Validated001BundleId, new LegacyBundleDisposition { Version = "0.0.1", Reason = "LEGACY_BROKEN_INTERACTIVE_BA" } },
                { Failed002BundleId, new LegacyBundleDisposition { Version = "0.0.2", Reason = "LEGACY_BROKEN_INTERACTIVE_BA" } },
                { C183002BundleId, new LegacyBundleDisposition { Version = "0.0.2", Reason = "LEGACY_EMBEDDED_APPLY_HWND_BUG" } },
                { Failed003BundleId, new LegacyBundleDisposition { Version = "0.0.3", Reason = "LEGACY_EMBEDDED_APPLY_HWND_BUG" } }
            };

        public static bool ShouldSuppress(RelatedBundleObservation related)
        {
            return TryGetSuppression(related, out _);
        }

        public static bool TryGetSuppression(RelatedBundleObservation related, out LegacyBundleDisposition disposition)
        {
            disposition = null;
            return related != null && related.Relation == RelationType.Upgrade &&
                !String.IsNullOrWhiteSpace(related.BundleId) && Unsafe.TryGetValue(related.BundleId, out disposition);
        }

        public static string NormalizeVersion(string value)
        {
            return Version.TryParse(value, out var version) ? version.ToString(3) : value;
        }
    }
}
