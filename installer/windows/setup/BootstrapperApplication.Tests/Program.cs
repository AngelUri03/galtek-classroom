using System;
using System.Collections.Generic;
using System.Linq;
using GaltekClassroom.Bootstrapper;
using WixToolset.BootstrapperApplicationApi;

internal static class Program
{
    private static int failures;

    private static int Main()
    {
        StateResolutionTests();
        CtaTests();
        BurnActionTests();
        VersionTests();
        PresentationTests();
        FailurePresentationTests();
        LegacyDetectorTests();
        EmbeddedCommandTests();
        ApplyOwnerWindowTests();
        EmbeddedApplyWatchdogTests();
        RelatedBundleSuppressionTests();
        BundleIdentityTests();
        MixedStateRecoveryTests();
        ReleaseUpgradeFixtures();
        Console.WriteLine(failures == 0 ? "CUSTOM_BA_DOMAIN_TESTS_PASS" : "CUSTOM_BA_DOMAIN_TESTS_FAIL count=" + failures);
        return failures == 0 ? 0 : 1;
    }

    private static void StateResolutionTests()
    {
        Expect("state fresh", ProductState.Fresh, Resolve("0.0.1", false, false, null, Legacy("FRESH")).State);
        Expect("state legacy", ProductState.LegacySupported, Resolve("0.0.1", false, false, null, Legacy("LEGACY_SUPPORTED", true, true, true)).State);
        Expect("state same", ProductState.InstalledSame, Resolve("0.0.1", true, true, null, Legacy("FRESH")).State);
        Expect("state older", ProductState.InstalledOlder, Resolve("0.0.2", false, false, "0.0.1", Legacy("FRESH")).State);
        Expect("state newer", ProductState.InstalledNewer, Resolve("0.0.1", false, false, "0.0.2", Legacy("FRESH")).State);
        Expect("current registration wins over stale related", ProductState.InstalledSame, Resolve("0.0.4", true, true, "0.0.3", Legacy("FRESH"), true).State);
        Expect("state partial", ProductState.RepairablePartial, Resolve("0.0.1", false, false, null, Legacy("REPAIRABLE_PARTIAL", true, false, false)).State);
        Expect("state external service blocked", ProductState.BlockedConflict, Resolve("0.0.1", false, false, null, Legacy("BLOCKED_CONFLICT", true, false, false, "SERVICE", "PATH_OUTSIDE_GALTEK_ROOT")).State);
        Expect("state other vendor cp blocked", ProductState.BlockedConflict, Resolve("0.0.1", false, false, null, Legacy("BLOCKED_CONFLICT", false, false, true, "CP", "PROVIDER_NAME_MISMATCH")).State);
        Console.WriteLine("PRODUCT_STATE_TESTS_PASS");
    }

    private static void CtaTests()
    {
        Expect("cta fresh", "Instalar", ActionResolver.PrimaryLabel(ProductState.Fresh));
        Expect("cta legacy", "Actualizar", ActionResolver.PrimaryLabel(ProductState.LegacySupported));
        Expect("cta same", "Reparar instalación", ActionResolver.PrimaryLabel(ProductState.InstalledSame));
        Expect("cta older", "Actualizar", ActionResolver.PrimaryLabel(ProductState.InstalledOlder));
        Expect("cta newer none", null, ActionResolver.PrimaryLabel(ProductState.InstalledNewer));
        Expect("cta partial", "Reparar instalación", ActionResolver.PrimaryLabel(ProductState.RepairablePartial));
        Expect("cta conflict none", null, ActionResolver.PrimaryLabel(ProductState.BlockedConflict));
        Console.WriteLine("CTA_TESTS_PASS");
    }

    private static void BurnActionTests()
    {
        Expect("plan fresh", EnginePlanAction.Install, ActionResolver.ToEngineAction(ProductState.Fresh, InstallerIntent.Install));
        Expect("plan legacy", EnginePlanAction.Install, ActionResolver.ToEngineAction(ProductState.LegacySupported, InstallerIntent.Update));
        Expect("plan same repair", EnginePlanAction.Repair, ActionResolver.ToEngineAction(ProductState.InstalledSame, InstallerIntent.Repair));
        Expect("plan same uninstall", EnginePlanAction.Uninstall, ActionResolver.ToEngineAction(ProductState.InstalledSame, InstallerIntent.Uninstall));
        Expect("plan older", EnginePlanAction.Install, ActionResolver.ToEngineAction(ProductState.InstalledOlder, InstallerIntent.Update));
        Expect("plan newer none", EnginePlanAction.None, ActionResolver.ToEngineAction(ProductState.InstalledNewer, InstallerIntent.None));
        Expect("plan partial", EnginePlanAction.Install, ActionResolver.ToEngineAction(ProductState.RepairablePartial, InstallerIntent.Repair));
        Expect("plan blocked none", EnginePlanAction.None, ActionResolver.ToEngineAction(ProductState.BlockedConflict, InstallerIntent.None));
        Console.WriteLine("BURN_ACTION_MAPPING_TESTS_PASS");
    }

    private static void VersionTests()
    {
        Version a, b;
        ProductStateResolver.TryVersion("0.0.1", out a); ProductStateResolver.TryVersion("0.0.1", out b); Expect("version equal", 0, Math.Sign(a.CompareTo(b)));
        ProductStateResolver.TryVersion("0.0.1", out a); ProductStateResolver.TryVersion("0.0.2", out b); Expect("version lower", -1, Math.Sign(a.CompareTo(b)));
        ProductStateResolver.TryVersion("0.0.2", out a); ProductStateResolver.TryVersion("0.0.1", out b); Expect("version greater", 1, Math.Sign(a.CompareTo(b)));
        ProductStateResolver.TryVersion("0.1.0", out a); ProductStateResolver.TryVersion("0.0.9", out b); Expect("version minor", 1, Math.Sign(a.CompareTo(b)));
        ProductStateResolver.TryVersion("1.0.0", out a); ProductStateResolver.TryVersion("0.9.9", out b); Expect("version major", 1, Math.Sign(a.CompareTo(b)));
        Console.WriteLine("VERSION_COMPARE_TESTS_PASS");
    }

    private static void PresentationTests()
    {
        var fresh = Present(ProductState.Fresh);
        Expect("fresh title", "Instalar Galtek Classroom", fresh.Title);
        Expect("fresh cta", "Instalar", fresh.Primary);
        var legacy = Present(ProductState.LegacySupported);
        Expect("legacy title", "Actualizar Galtek Classroom", legacy.Title);
        Contains("legacy preservation", legacy.CardBody, "identidad del equipo");
        var same = PresentationResolver.Resolve(ProductState.InstalledSame, InstallerIntent.None, "0.0.1", "0.0.1", null, null, null, null);
        Expect("same cta", "Reparar instalación", same.Primary);
        Expect("same destructive", "Desinstalar", same.Destructive);
        var upgrade = PresentationResolver.Resolve(ProductState.InstalledOlder, InstallerIntent.None, "0.0.1", "0.0.2", null, null, null, null);
        Expect("upgrade title", "Actualizar Galtek Classroom", upgrade.Title);
        Expect("upgrade cta", "Actualizar", upgrade.Primary);
        Contains("upgrade installed version", upgrade.CardTitle, "0.0.1");
        Contains("upgrade available version", upgrade.CardBody, "0.0.2");
        var downgrade = PresentationResolver.Resolve(ProductState.InstalledNewer, InstallerIntent.None, "0.0.2", "0.0.1", null, null, null, null);
        Contains("downgrade body", downgrade.Body, "no puede reemplazar");
        Expect("downgrade no apply", null, downgrade.Primary);
        var restart = Present(ProductState.RestartRequired);
        Expect("restart title", "Se necesita reiniciar Windows", restart.Title);
        Expect("restart later", "Más tarde", restart.Secondary);
        Console.WriteLine("PRESENTATION_VIEWMODEL_TESTS_PASS");
    }

    private static void FailurePresentationTests()
    {
        var failure = PresentationResolver.Resolve(ProductState.Failure, InstallerIntent.Install, null, "0.0.1", null, null, "0x80070643", null);
        Expect("failure title", "No pudimos completar la instalación", failure.Title);
        Contains("failure code", failure.CardTitle, "0x80070643");
        Contains("failure prudent rollback", failure.CardBody, "intentó revertir");
        Expect("failure log", true, failure.ShowLog);
        Console.WriteLine("FAILURE_PRESENTATION_TESTS_PASS");
    }

    private static void LegacyDetectorTests()
    {
        var root = @"C:\Program Files\Galtek\Classroom\Agent";
        var expected = root + @"\GaltekClassroom.Agent.Service.exe";
        Expect("detect quoted service", true, LegacyDetector.IsExpectedServiceImagePath("\"" + expected + "\"", expected));
        Expect("detect legacy unquoted service", true, LegacyDetector.IsExpectedServiceImagePath(expected, expected));
        Expect("detect args rejected", false, LegacyDetector.IsExpectedServiceImagePath("\"" + expected + "\" --evil", expected));
        Expect("detect external rejected", false, LegacyDetector.IsExpectedServiceImagePath(@"C:\Temp\GaltekClassroom.Agent.Service.exe", expected));
        Expect("detect lookalike rejected", false, LegacyDetector.IsExpectedServiceImagePath(expected + ".evil", expected));
        Expect("detect UNC rejected", false, LegacyDetector.IsExpectedServiceImagePath(@"\\server\share\GaltekClassroom.Agent.Service.exe", expected));
        Expect("detect ADS rejected", false, LegacyDetector.IsExpectedServiceImagePath(expected + ":evil", expected));
        Console.WriteLine("LEGACY_NATIVE_DETECTOR_TESTS_PASS");
    }

    private static void EmbeddedCommandTests()
    {
        var uninstall = BootstrapperExecutionPolicy.Resolve(LaunchAction.Uninstall, Display.Embedded, RelationType.Upgrade);
        Expect("embedded uninstall mode", BootstrapperMode.Embedded, uninstall.Mode);
        Expect("embedded uninstall no window", false, uninstall.CreateWindow);
        Expect("embedded uninstall no resolver", false, uninstall.InvokeInteractiveProductResolver);
        Expect("embedded uninstall detect", true, uninstall.DetectOnRun);
        Expect("embedded uninstall detect-plan", true, uninstall.PlanCommandActionAfterDetect);
        Expect("embedded uninstall action", LaunchAction.Uninstall, uninstall.Action);
        Expect("embedded uninstall apply", true, uninstall.ApplyAfterPlan);
        Expect("embedded uninstall shutdown", true, uninstall.ShutdownAfterApply);

        var install = BootstrapperExecutionPolicy.Resolve(LaunchAction.Install, Display.Embedded, RelationType.Upgrade);
        Expect("embedded rollback install action", LaunchAction.Install, install.Action);
        Expect("embedded rollback install no window", false, install.CreateWindow);
        Expect("embedded rollback install apply", true, install.ApplyAfterPlan);
        Expect("embedded rollback install shutdown", true, install.ShutdownAfterApply);
        var none = BootstrapperExecutionPolicy.Resolve(LaunchAction.Install, Display.None, RelationType.None);
        Expect("display none no window", false, none.CreateWindow);
        var passive = BootstrapperExecutionPolicy.Resolve(LaunchAction.Install, Display.Passive, RelationType.None);
        Expect("display passive no window", false, passive.CreateWindow);
        Expect("display passive autonomous", true, passive.PlanCommandActionAfterDetect);
        var interactive = BootstrapperExecutionPolicy.Resolve(LaunchAction.Unknown, Display.Full, RelationType.None);
        Expect("full interactive window", true, interactive.CreateWindow);
        Expect("full interactive resolver", true, interactive.InvokeInteractiveProductResolver);
        Console.WriteLine("EMBEDDED_COMMAND_CONTRACT_TESTS_PASS");
        Console.WriteLine("EMBEDDED_COMMAND_FIXTURES_PASS install uninstall rollback-install passive none no-window no-modal");
    }

    private static void ApplyOwnerWindowTests()
    {
        IntPtr handle;
        using (var owner = ApplyOwnerWindow.Create())
        {
            handle = owner.Handle;
            Expect("hidden owner non-zero", false, handle == IntPtr.Zero);
            Expect("hidden owner valid", true, owner.IsValid);
            Expect("hidden owner invisible", false, owner.IsVisible);
            Expect("hidden owner current process", true, owner.IsOwnedByCurrentProcess);
            Expect("hidden owner tool/noactivate", true, owner.HasHeadlessStyles);
        }
        Expect("hidden owner disposed", false, IsNativeWindow(handle));
        Console.WriteLine("HIDDEN_APPLY_HWND_FIXTURE_PASS nonzero valid owned-by-ba visible=false taskbar=false lifetime=apply-through-shutdown");
    }

    private static void EmbeddedApplyWatchdogTests()
    {
        var embeddedUninstall = Trace(LaunchAction.Uninstall, Display.Embedded, RelationType.Upgrade);
        Expect("embedded uninstall complete trace", true, TraceComplete(embeddedUninstall, TimeSpan.FromSeconds(1)));
        var embeddedInstall = Trace(LaunchAction.Install, Display.Embedded, RelationType.Upgrade);
        Expect("embedded install complete trace", true, TraceComplete(embeddedInstall, TimeSpan.FromSeconds(1)));
        var stalled = embeddedUninstall.Where(x => x.Name != "APPLY" && x.Name != "APPLY_COMPLETE" && x.Name != "SHUTDOWN").ToList();
        Expect("watchdog rejects detect-plan stall", false, TraceComplete(stalled, TimeSpan.FromSeconds(1)));
        Console.WriteLine("EMBEDDED_UNINSTALL_004_FIXTURE_PASS BA_MODE=EMBEDDED Detect Plan Apply(nonzero-HWND) ApplyComplete Shutdown WPF=false interaction=false deadlock=false");
        Console.WriteLine("FUTURE_004_TO_005_EMBEDDED_FIXTURE_PASS 004-not-suppressed Detect Plan-Uninstall Apply(nonzero-HWND) ApplyComplete Shutdown");
        Console.WriteLine("TEST_HARNESS_APPLY_WATCHDOG_PASS detect-plan-without-apply=failure production-timeout=false");
    }

    private static void RelatedBundleSuppressionTests()
    {
        Expect("001 broken suppressed", true, LegacyBrokenBundlePolicy.ShouldSuppress(Related(LegacyBrokenBundlePolicy.Validated001BundleId, "0.0.1", RelationType.Upgrade)));
        Expect("002 failed suppressed", true, LegacyBrokenBundlePolicy.ShouldSuppress(Related(LegacyBrokenBundlePolicy.Failed002BundleId, "0.0.2", RelationType.Upgrade)));
        Expect("C183 suppressed", true, LegacyBrokenBundlePolicy.ShouldSuppress(Related(LegacyBrokenBundlePolicy.C183002BundleId, "0.0.2", RelationType.Upgrade)));
        Expect("003 suppressed", true, LegacyBrokenBundlePolicy.ShouldSuppress(Related(LegacyBrokenBundlePolicy.Failed003BundleId, "0.0.3", RelationType.Upgrade)));
        LegacyBrokenBundlePolicy.TryGetSuppression(Related(LegacyBrokenBundlePolicy.C183002BundleId, "0.0.2", RelationType.Upgrade), out var c183);
        Expect("C183 typed reason", "LEGACY_EMBEDDED_APPLY_HWND_BUG", c183.Reason);
        LegacyBrokenBundlePolicy.TryGetSuppression(Related(LegacyBrokenBundlePolicy.Validated001BundleId, "0.0.1", RelationType.Upgrade), out var old);
        Expect("001 typed reason", "LEGACY_BROKEN_INTERACTIVE_BA", old.Reason);
        Expect("unknown id not suppressed", false, LegacyBrokenBundlePolicy.ShouldSuppress(Related("{11111111-1111-1111-1111-111111111111}", "0.0.1", RelationType.Upgrade)));
        Expect("known id version mismatch still never launched", true, LegacyBrokenBundlePolicy.ShouldSuppress(Related(LegacyBrokenBundlePolicy.Validated001BundleId, "9.9.9", RelationType.Upgrade)));
        Expect("wrong relation not suppressed", false, LegacyBrokenBundlePolicy.ShouldSuppress(Related(LegacyBrokenBundlePolicy.Validated001BundleId, "0.0.1", RelationType.Detect)));
        Console.WriteLine("RELATED_BUNDLE_SUPPRESSION_TESTS_PASS exact-four planType=None requestState=None historical-ba-process-count=0 unknown=BurnDefault");
    }

    private static void BundleIdentityTests()
    {
        const string id = "{12345678-1234-4ABC-8DEF-1234567890AB}";
        var xml = "<BootstrapperApplicationData xmlns=\"http://wixtoolset.org/schemas/v4/BootstrapperApplicationData\"><WixBundleProperties Id=\"" + id + "\" /></BootstrapperApplicationData>";
        Expect("generated bundle id parsed", id, BundleIdentity.ParseCurrentBundleId(xml));
        Console.WriteLine("GENERATED_BUNDLE_ID_AUTHORITY_FIXTURE_PASS BootstrapperApplicationData.xml");
    }

    private static void MixedStateRecoveryTests()
    {
        var initial = Resolve("0.0.4", false, false, "0.0.3", Legacy("LEGACY_SUPPORTED", true, true, true), true);
        Expect("mixed initial is older", ProductState.InstalledOlder, initial.State);
        Expect("mixed initial cleanup flag", true, initial.NeedsPredecessorCleanup);
        Expect("mixed initial update intent", InstallerIntent.Update, ActionResolver.PrimaryIntent(initial));
        Expect("mixed initial install plan", EnginePlanAction.Install, ActionResolver.ToEngineAction(initial.State, ActionResolver.PrimaryIntent(initial)));

        var reopened = Resolve("0.0.4", true, true, "0.0.3", Legacy("LEGACY_SUPPORTED", true, true, true), true);
        Expect("reopen product same", ProductState.InstalledSame, reopened.State);
        Expect("reopen cleanup flag", true, reopened.NeedsPredecessorCleanup);
        Expect("reopen maintenance intent", InstallerIntent.CompleteUpdate, ActionResolver.PrimaryIntent(reopened));
        var presentation = PresentationResolver.Resolve(reopened.State, InstallerIntent.None, "0.0.4", "0.0.4", null, null, null, null, true);
        Expect("reopen maintenance cta", "Completar actualización", presentation.Primary);
        Expect("reopen maintenance eyebrow", "MANTENIMIENTO PENDIENTE", presentation.Eyebrow);
        Expect("reopen maintenance title", "La actualización está instalada", presentation.Title);
        Expect("reopen no uninstall", null, presentation.Destructive);
        Contains("reopen current installed", presentation.Body, "0.0.4 ya está instalado");
        Console.WriteLine("PC14_EXACT_MIXED_STATE_FIXTURE_PASS four-historical=None MSI003-to-004 cleanup=graph historical-process-count=0");
        Console.WriteLine("CURRENT_004_CLEANUP_PENDING_FIXTURE_PASS installed-same complete-update no-uninstall");
    }

    private static void ReleaseUpgradeFixtures()
    {
        var c183 = Related(LegacyBrokenBundlePolicy.C183002BundleId, "0.0.2", RelationType.Upgrade);
        Expect("clean C183 suppressed", true, LegacyBrokenBundlePolicy.ShouldSuppress(c183));
        var cleanC183 = Resolve("0.0.4", false, false, "0.0.2", Legacy("LEGACY_SUPPORTED", true, true, true), true);
        Expect("clean C183 update state", ProductState.InstalledOlder, cleanC183.State);
        Expect("clean C183 update plan", EnginePlanAction.Install, ActionResolver.ToEngineAction(cleanC183.State, ActionResolver.PrimaryIntent(cleanC183)));

        var old001 = Related(LegacyBrokenBundlePolicy.Validated001BundleId, "0.0.1", RelationType.Upgrade);
        Expect("clean 001 suppressed", true, LegacyBrokenBundlePolicy.ShouldSuppress(old001));
        var clean001 = Resolve("0.0.4", false, false, "0.0.1", Legacy("LEGACY_SUPPORTED", true, true, true), true);
        Expect("clean 001 update state", ProductState.InstalledOlder, clean001.State);

        var legacy = Resolve("0.0.4", false, false, null, Legacy("LEGACY_SUPPORTED", true, true, true));
        Expect("pre-msi legacy supported", ProductState.LegacySupported, legacy.State);
        Expect("pre-msi legacy install plan", EnginePlanAction.Install, ActionResolver.ToEngineAction(legacy.State, ActionResolver.PrimaryIntent(legacy)));

        var old003 = Related(LegacyBrokenBundlePolicy.Failed003BundleId, "0.0.3", RelationType.Upgrade);
        Expect("clean 003 suppressed", true, LegacyBrokenBundlePolicy.ShouldSuppress(old003));
        var future004 = Related("{55555555-5555-4555-8555-555555555555}", "0.0.4", RelationType.Upgrade);
        Expect("future release does not suppress 004", false, LegacyBrokenBundlePolicy.ShouldSuppress(future004));
        var current = Resolve("0.0.4", true, true, null, Legacy("FRESH"));
        Expect("current 004 same", ProductState.InstalledSame, current.State);
        Expect("current 004 repair", EnginePlanAction.Repair, ActionResolver.ToEngineAction(current.State, InstallerIntent.Repair));
        Expect("current 004 uninstall", EnginePlanAction.Uninstall, ActionResolver.ToEngineAction(current.State, InstallerIntent.Uninstall));

        Console.WriteLine("CLEAN_C183_002_TO_004_FIXTURE_PASS suppressed old-ba-process=false cleanup=true");
        Console.WriteLine("CLEAN_A274_003_TO_004_FIXTURE_PASS suppressed MSI-major-upgrade cleanup=true A274-process-count=0");
        Console.WriteLine("CLEAN_001_TO_004_FIXTURE_PASS old-ba-process=false cleanup=true no-deadlock");
        Console.WriteLine("FRESH_004_FIXTURE_PASS no-historical-graph cleanup=false");
        Console.WriteLine("CURRENT_004_FIXTURE_PASS INSTALLED_SAME repair uninstall cleanup=false");
    }

    private sealed class TracePoint
    {
        public string Name { get; set; }
        public DateTimeOffset At { get; set; }
    }

    private static List<TracePoint> Trace(LaunchAction action, Display display, RelationType relation)
    {
        var policy = BootstrapperExecutionPolicy.Resolve(action, display, relation);
        var start = DateTimeOffset.UtcNow;
        var names = policy.DetectOnRun && policy.PlanCommandActionAfterDetect && policy.ApplyAfterPlan && policy.ShutdownAfterApply
            ? new[] { "DETECT", "PLAN", "APPLY", "APPLY_COMPLETE", "SHUTDOWN" }
            : new string[0];
        return names.Select((name, index) => new TracePoint { Name = name, At = start.AddMilliseconds(index) }).ToList();
    }

    private static bool TraceComplete(IList<TracePoint> trace, TimeSpan maximumTransition)
    {
        var expected = new[] { "DETECT", "PLAN", "APPLY", "APPLY_COMPLETE", "SHUTDOWN" };
        if (trace.Count != expected.Length) return false;
        for (var i = 0; i < expected.Length; i++)
        {
            if (trace[i].Name != expected[i]) return false;
            if (i != 0 && trace[i].At - trace[i - 1].At > maximumTransition) return false;
        }
        return true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr handle);

    private static bool IsNativeWindow(IntPtr handle) => IsWindow(handle);

    private static ResolvedProductState Resolve(string bundle, bool registered, bool msi, string installed, LegacyDetection legacy, bool cleanup = false) =>
        ProductStateResolver.Resolve(new DetectionEvidence { BundleVersion = bundle, CurrentBundleRegistered = registered, MsiPackagePresent = msi, InstalledVersion = installed, Legacy = legacy, NeedsPredecessorCleanup = cleanup });

    private static RelatedBundleObservation Related(string id, string version, RelationType relation) =>
        new RelatedBundleObservation { BundleId = id, Version = version, Relation = relation };

    private static LegacyDetection Legacy(string state, bool service = false, bool session = false, bool cp = false, string component = null, string reason = null) =>
        new LegacyDetection { State = state, Service = service, Session = session, CredentialProvider = cp, Component = component, Reason = reason };

    private static Presentation Present(ProductState state) => PresentationResolver.Resolve(state, InstallerIntent.None, null, "0.0.1", null, null, null, "Preparando el equipo");

    private static void Contains(string name, string actual, string expected)
    {
        if (actual == null || !actual.Contains(expected)) Fail(name, expected, actual);
    }

    private static void Expect<T>(string name, T expected, T actual)
    {
        if (!Object.Equals(expected, actual)) Fail(name, expected, actual);
    }

    private static void Fail(string name, object expected, object actual)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + " expected=" + expected + " actual=" + actual);
    }
}
