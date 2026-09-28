using System;
using System.Runtime.Serialization;

namespace GaltekClassroom.Bootstrapper
{
    public enum ProductState
    {
        Boot,
        Detecting,
        Fresh,
        LegacySupported,
        InstalledSame,
        InstalledOlder,
        InstalledNewer,
        RepairablePartial,
        BlockedConflict,
        DetectionFailed,
        Planning,
        ApplyingInstall,
        ApplyingUpdate,
        ApplyingRepair,
        ApplyingUninstall,
        Success,
        CleanupPending,
        Failure,
        RestartRequired
    }

    public enum InstallerIntent
    {
        None,
        Install,
        Update,
        CompleteUpdate,
        Repair,
        Uninstall
    }

    public enum EnginePlanAction
    {
        None,
        Install,
        Repair,
        Uninstall
    }

    [DataContract]
    public sealed class LegacyDetection
    {
        [DataMember(Name = "state")]
        public string State { get; set; }
        [DataMember(Name = "component")]
        public string Component { get; set; }
        [DataMember(Name = "reason")]
        public string Reason { get; set; }
        [DataMember(Name = "service")]
        public bool Service { get; set; }
        [DataMember(Name = "session")]
        public bool Session { get; set; }
        [DataMember(Name = "credentialProvider")]
        public bool CredentialProvider { get; set; }
    }

    public sealed class DetectionEvidence
    {
        public string BundleVersion { get; set; }
        public bool CurrentBundleRegistered { get; set; }
        public bool MsiPackagePresent { get; set; }
        public string InstalledVersion { get; set; }
        public LegacyDetection Legacy { get; set; }
        public bool NeedsPredecessorCleanup { get; set; }
    }

    public sealed class ResolvedProductState
    {
        public ProductState State { get; set; }
        public string InstalledVersion { get; set; }
        public string AvailableVersion { get; set; }
        public string Component { get; set; }
        public string Reason { get; set; }
        public bool NeedsPredecessorCleanup { get; set; }
    }

    public static class ProductStateResolver
    {
        public static ResolvedProductState Resolve(DetectionEvidence evidence)
        {
            if (evidence == null || !TryVersion(evidence.BundleVersion, out var available))
            {
                return Failed("BUNDLE_VERSION_UNAVAILABLE");
            }

            // A full registration or the exact chained MSI is authoritative for
            // reopening this version. Stale related bundles must not downgrade an
            // already-current installation to INSTALLED_OLDER.
            if (evidence.CurrentBundleRegistered || evidence.MsiPackagePresent)
            {
                return new ResolvedProductState
                {
                    State = ProductState.InstalledSame,
                    InstalledVersion = available.ToString(3),
                    AvailableVersion = available.ToString(3),
                    NeedsPredecessorCleanup = evidence.NeedsPredecessorCleanup
                };
            }

            if (!String.IsNullOrWhiteSpace(evidence.InstalledVersion))
            {
                if (!TryVersion(evidence.InstalledVersion, out var installed))
                {
                    return Failed("INSTALLED_VERSION_INVALID");
                }

                var comparison = installed.CompareTo(available);
                return new ResolvedProductState
                {
                    State = comparison == 0 ? ProductState.InstalledSame :
                        comparison < 0 ? ProductState.InstalledOlder : ProductState.InstalledNewer,
                    InstalledVersion = installed.ToString(3),
                    AvailableVersion = available.ToString(3),
                    NeedsPredecessorCleanup = evidence.NeedsPredecessorCleanup
                };
            }

            if (evidence.Legacy == null)
            {
                return Failed("LEGACY_DETECTION_UNAVAILABLE");
            }

            switch ((evidence.Legacy.State ?? String.Empty).ToUpperInvariant())
            {
                case "FRESH":
                    return Ready(ProductState.Fresh, available);
                case "LEGACY_SUPPORTED":
                    return Ready(ProductState.LegacySupported, available);
                case "REPAIRABLE_PARTIAL":
                    return Ready(ProductState.RepairablePartial, available);
                case "BLOCKED_CONFLICT":
                    return new ResolvedProductState
                    {
                        State = ProductState.BlockedConflict,
                        AvailableVersion = available.ToString(3),
                        Component = evidence.Legacy.Component,
                        Reason = evidence.Legacy.Reason
                    };
                default:
                    return Failed("LEGACY_DETECTION_INVALID");
            }
        }

        public static bool TryVersion(string value, out Version version)
        {
            version = null;
            if (String.IsNullOrWhiteSpace(value) || !Version.TryParse(value, out var parsed)) return false;
            if (parsed.Major < 0 || parsed.Minor < 0 || parsed.Build < 0) return false;
            version = parsed;
            return true;
        }

        private static ResolvedProductState Ready(ProductState state, Version available)
        {
            return new ResolvedProductState { State = state, AvailableVersion = available.ToString(3) };
        }

        private static ResolvedProductState Failed(string reason)
        {
            return new ResolvedProductState { State = ProductState.DetectionFailed, Reason = reason };
        }
    }

    public static class ActionResolver
    {
        public static string PrimaryLabel(ProductState state)
        {
            switch (state)
            {
                case ProductState.Fresh: return "Instalar";
                case ProductState.LegacySupported:
                case ProductState.InstalledOlder: return "Actualizar";
                case ProductState.InstalledSame:
                case ProductState.RepairablePartial: return "Reparar instalación";
                default: return null;
            }
        }

        public static InstallerIntent PrimaryIntent(ProductState state)
        {
            switch (state)
            {
                case ProductState.Fresh: return InstallerIntent.Install;
                case ProductState.LegacySupported:
                case ProductState.InstalledOlder: return InstallerIntent.Update;
                case ProductState.InstalledSame:
                case ProductState.RepairablePartial: return InstallerIntent.Repair;
                default: return InstallerIntent.None;
            }
        }

        public static InstallerIntent PrimaryIntent(ResolvedProductState resolved)
        {
            if (resolved != null && resolved.State == ProductState.InstalledSame && resolved.NeedsPredecessorCleanup)
                return InstallerIntent.CompleteUpdate;
            return PrimaryIntent(resolved == null ? ProductState.DetectionFailed : resolved.State);
        }

        public static EnginePlanAction ToEngineAction(ProductState state, InstallerIntent intent)
        {
            if (intent == InstallerIntent.CompleteUpdate && state == ProductState.InstalledSame) return EnginePlanAction.Install;
            if (intent == InstallerIntent.Uninstall && state == ProductState.InstalledSame) return EnginePlanAction.Uninstall;
            switch (state)
            {
                case ProductState.Fresh:
                case ProductState.LegacySupported:
                case ProductState.InstalledOlder:
                case ProductState.RepairablePartial:
                    return EnginePlanAction.Install;
                case ProductState.InstalledSame:
                    return intent == InstallerIntent.Repair ? EnginePlanAction.Repair : EnginePlanAction.None;
                default:
                    return EnginePlanAction.None;
            }
        }
    }
}
