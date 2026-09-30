using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WixToolset.BootstrapperApplicationApi;

namespace GaltekClassroom.Bootstrapper
{
    public sealed class GaltekBootstrapperApplication : BootstrapperApplication
    {
        private const string PackageId = "GaltekClassroomClientMsi";
        private const string CleanupPackageId = "LegacyBundleRegistrationCleanup";
        private const string CleanupLogVariable = "GaltekCleanupLogPath";
        private readonly object detectionGate = new object();
        private readonly Dictionary<string, RelatedBundleObservation> relatedBundles = new Dictionary<string, RelatedBundleObservation>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> suppressedBundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private IBootstrapperCommand command;
        private BootstrapperExecutionPlan execution;
        private Dispatcher dispatcher;
        private MainWindow window;
        private InstallerViewModel viewModel;
        private ApplyOwnerWindow headlessApplyOwner;
        private LegacyDetection legacyDetection;
        private bool legacyComplete;
        private bool burnComplete;
        private bool currentBundleRegistered;
        private bool msiPresent;
        private bool cleanupPackageFailed;
        private string relatedVersion;
        private string bundleVersion;
        private string currentBundleId;
        private string cleanupLogPath;
        private InstallerIntent activeIntent;
        private int result;

        internal IEngine Engine => this.engine;

        protected override void OnCreate(CreateEventArgs args)
        {
            base.OnCreate(args);
            this.command = args.Command;
            this.execution = BootstrapperExecutionPolicy.Resolve(args.Command.Action, args.Command.Display, args.Command.Relation);
        }

        protected override void Run()
        {
            this.Engine.Log(LogLevel.Standard, "BA_START runtime=net48 ui=WPF");
            this.Engine.Log(LogLevel.Standard, "BA_MODE=" + this.execution.Mode.ToString().ToUpperInvariant() +
                " action=" + this.command.Action + " display=" + this.command.Display + " relation=" + this.command.Relation);
            try
            {
                this.dispatcher = Dispatcher.CurrentDispatcher;
                this.bundleVersion = this.Engine.GetVariableVersion("WixBundleVersion");
                this.currentBundleId = BundleIdentity.ReadCurrentBundleId(AppDomain.CurrentDomain.BaseDirectory);
                this.Engine.SetVariableString("GaltekCurrentBundleId", this.currentBundleId, false);
                this.Engine.Log(LogLevel.Standard, "CURRENT_BUNDLE_AUTHORITY bundleId=" + this.currentBundleId +
                    " version=" + this.bundleVersion + " providerKey=GaltekSolution.GaltekClassroom.Client.Bundle.0.0.6");
                Subscribe();

                if (this.execution.CreateWindow)
                {
                    var wpfApplication = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    wpfApplication.Properties["Bootstrapper"] = this;
                    this.viewModel = new InstallerViewModel(this, this.bundleVersion);
                    this.window = new MainWindow(this.viewModel);
                    this.viewModel.WindowHandle = new WindowInteropHelper(this.window).EnsureHandle();
                    this.window.Show();
                    _ = DetectLegacyAsync();
                }
                else
                {
                    this.Engine.CloseSplashScreen();
                    this.legacyDetection = new LegacyDetection { State = "FRESH", Reason = "HEADLESS_COMMAND" };
                    lock (this.detectionGate) this.legacyComplete = true;
                }

                this.Engine.Log(LogLevel.Standard, "DETECT_BEGIN");
                this.Engine.Detect();
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                this.result = System.Runtime.InteropServices.Marshal.GetHRForException(ex);
                var detail = ex.GetType().Name + ": " + ex.Message;
                if (ex.InnerException != null) detail += " | " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
                this.Engine.Log(LogLevel.Error, "BA_FATAL code=" + FormatCode(this.result) + " reason=" + Sanitize(detail));
            }
            try
            {
                this.Engine.Log(LogLevel.Standard, "BA_SHUTDOWN");
                this.Engine.Quit(NormalizeExitCode(this.result));
            }
            finally
            {
                this.headlessApplyOwner?.Dispose();
                this.headlessApplyOwner = null;
            }
        }

        private void Subscribe()
        {
            this.DetectBegin += OnDetectBegin;
            this.DetectRelatedBundle += OnDetectRelatedBundle;
            this.DetectPackageComplete += OnDetectPackageComplete;
            this.DetectComplete += OnDetectComplete;
            this.PlanBegin += (s, e) => DispatchViewModel(vm => vm.SetStage("Calculando cambios"));
            this.PlanRelatedBundleType += OnPlanRelatedBundleType;
            this.PlanRelatedBundle += OnPlanRelatedBundle;
            this.PlanRestoreRelatedBundle += OnPlanRestoreRelatedBundle;
            this.PlanPackageBegin += (s, e) => DispatchViewModel(vm => vm.SetStage("Revisando componentes"));
            this.PlanComplete += OnPlanComplete;
            this.ApplyBegin += OnApplyBegin;
            this.CacheAcquireBegin += (s, e) => DispatchViewModel(vm => vm.SetStage("Preparando componentes"));
            this.CacheAcquireProgress += (s, e) => { e.Cancel = e.Cancel || IsCancelRequested; DispatchViewModel(vm => vm.SetProgress(e.OverallPercentage)); };
            this.CacheVerifyProgress += (s, e) => { e.Cancel = e.Cancel || IsCancelRequested; DispatchViewModel(vm => vm.SetProgress(e.OverallPercentage)); };
            this.CachePayloadExtractProgress += (s, e) => { e.Cancel = e.Cancel || IsCancelRequested; DispatchViewModel(vm => vm.SetProgress(e.OverallPercentage)); };
            this.ExecutePackageBegin += OnExecutePackageBegin;
            this.ExecuteProgress += OnExecuteProgress;
            this.ExecutePackageComplete += OnExecutePackageComplete;
            this.Progress += (s, e) => e.Cancel = e.Cancel || IsCancelRequested;
            this.Error += OnError;
            this.ApplyComplete += OnApplyComplete;
        }

        private bool IsCancelRequested => this.viewModel != null && this.viewModel.CancelRequested;

        private async Task DetectLegacyAsync()
        {
            try
            {
                this.legacyDetection = await Task.Run(() => LegacyDetector.Detect()).ConfigureAwait(false);
                this.Engine.Log(LogLevel.Standard, "LEGACY_DETECTION state=" + this.legacyDetection.State + " service=" + this.legacyDetection.Service.ToString().ToLowerInvariant() + " session=" + this.legacyDetection.Session.ToString().ToLowerInvariant() + " cp=" + this.legacyDetection.CredentialProvider.ToString().ToLowerInvariant());
            }
            catch (Exception ex)
            {
                this.Engine.Log(LogLevel.Error, "LEGACY_DETECTION_FAILED reason=" + Sanitize(ex.Message));
                this.legacyDetection = new LegacyDetection { State = "DETECTION_FAILED", Reason = "LEGACY_DETECTION_FAILED" };
            }
            finally
            {
                lock (this.detectionGate) this.legacyComplete = true;
                TryResolveDetection();
            }
        }

        private void OnDetectBegin(object sender, DetectBeginEventArgs e)
        {
            this.currentBundleRegistered = e.RegistrationType == RegistrationType.Full;
        }

        private void OnDetectRelatedBundle(object sender, DetectRelatedBundleEventArgs e)
        {
            var observation = new RelatedBundleObservation
            {
                BundleId = e.ProductCode,
                Version = LegacyBrokenBundlePolicy.NormalizeVersion(e.Version),
                Relation = e.RelationType
            };
            lock (this.detectionGate) this.relatedBundles[e.ProductCode] = observation;
            if (e.RelationType != RelationType.Upgrade || String.IsNullOrWhiteSpace(e.Version)) return;
            if (String.IsNullOrWhiteSpace(this.relatedVersion) || CompareVersions(e.Version, this.relatedVersion) > 0)
                this.relatedVersion = e.Version;
        }

        private void OnDetectPackageComplete(object sender, DetectPackageCompleteEventArgs e)
        {
            if (String.Equals(e.PackageId, PackageId, StringComparison.Ordinal))
                this.msiPresent = e.State == PackageState.Present || e.State == PackageState.Superseded;
        }

        private void OnDetectComplete(object sender, DetectCompleteEventArgs e)
        {
            if (e.Status < 0)
            {
                this.result = e.Status;
                this.legacyDetection = new LegacyDetection { State = "DETECTION_FAILED", Reason = FormatCode(e.Status) };
                lock (this.detectionGate) this.legacyComplete = true;
            }
            lock (this.detectionGate) this.burnComplete = true;
            this.Engine.Log(e.Status < 0 ? LogLevel.Error : LogLevel.Standard, "DETECT_COMPLETE result=" + FormatCode(e.Status));

            if (this.execution.PlanCommandActionAfterDetect)
            {
                if (e.Status < 0) Close();
                else
                {
                    if (this.command.Relation == RelationType.None) ConfigureCleanupVariables();
                    Dispatch(() => PlanCommandAction(this.execution.Action));
                }
                return;
            }
            TryResolveDetection();
        }

        private void TryResolveDetection()
        {
            if (!this.execution.InvokeInteractiveProductResolver) return;
            List<RelatedBundleObservation> observations;
            lock (this.detectionGate)
            {
                if (!this.burnComplete || !this.legacyComplete) return;
                observations = this.relatedBundles.Values.ToList();
            }

            var needsCleanup = ConfigureCleanupVariables();
            var resolved = ProductStateResolver.Resolve(new DetectionEvidence
            {
                BundleVersion = this.bundleVersion,
                CurrentBundleRegistered = this.currentBundleRegistered,
                MsiPackagePresent = this.msiPresent,
                InstalledVersion = this.relatedVersion,
                Legacy = this.legacyDetection,
                NeedsPredecessorCleanup = needsCleanup
            });
            this.Engine.Log(LogLevel.Standard, "PRODUCT_STATE_RESOLVED state=" + StateLogName(resolved.State) +
                " predecessorCleanup=" + needsCleanup.ToString().ToLowerInvariant());
            DispatchViewModel(vm => vm.SetResolvedState(resolved));
        }

        private bool ConfigureCleanupVariables()
        {
            List<RelatedBundleObservation> observations;
            lock (this.detectionGate) observations = this.relatedBundles.Values.ToList();
            var broken = observations.Where(LegacyBrokenBundlePolicy.ShouldSuppress).ToList();
            var needsCleanup = broken.Count != 0;
            this.Engine.SetVariableNumeric("GaltekNeedsPredecessorCleanup", needsCleanup ? 1 : 0);
            this.Engine.SetVariableString("GaltekLegacyPredecessors", String.Join(";", broken.Select(x => x.BundleId)), false);
            if (needsCleanup)
            {
                if (String.IsNullOrWhiteSpace(this.cleanupLogPath)) this.cleanupLogPath = CreateCleanupLogPath();
                this.Engine.SetVariableString(CleanupLogVariable, this.cleanupLogPath, false);
            }
            return needsCleanup;
        }

        internal void Plan(InstallerIntent intent)
        {
            var state = this.viewModel.ProductState;
            var action = ActionResolver.ToEngineAction(state, intent);
            if (action == EnginePlanAction.None) return;
            this.activeIntent = intent;
            this.Engine.Log(LogLevel.Standard, "USER_ACTION action=" + intent.ToString().ToUpperInvariant());
            this.viewModel.SetPlanning(intent);
            var launchAction = action == EnginePlanAction.Repair ? LaunchAction.Repair :
                action == EnginePlanAction.Uninstall ? LaunchAction.Uninstall : LaunchAction.Install;
            PlanCommandAction(launchAction, false);
        }

        private void PlanCommandAction(LaunchAction action, bool setIntent = true)
        {
            if (action == LaunchAction.Unknown) { this.result = unchecked((int)0x80070057); Close(); return; }
            if (setIntent) this.activeIntent = IntentFor(action);
            this.Engine.Log(LogLevel.Standard, "PLAN_BEGIN action=" + action.ToString().ToUpperInvariant());
            this.Engine.Plan(action);
        }

        private void OnPlanRelatedBundleType(object sender, PlanRelatedBundleTypeEventArgs e)
        {
            if (!TryGetBrokenRelated(e.BundleId, out var related)) return;
            e.Type = RelatedBundlePlanType.None;
            lock (this.detectionGate) this.suppressedBundles.Add(e.BundleId);
            LegacyBrokenBundlePolicy.TryGetSuppression(related, out var disposition);
            this.Engine.Log(LogLevel.Standard, "RELATED_BUNDLE_SUPPRESSED bundleId=" + e.BundleId +
                " version=" + related.Version + " reason=" + disposition.Reason + " planType=None");
        }

        private void OnPlanRelatedBundle(object sender, PlanRelatedBundleEventArgs e)
        {
            if (IsSuppressed(e.BundleId)) e.State = RequestState.None;
        }

        private void OnPlanRestoreRelatedBundle(object sender, PlanRestoreRelatedBundleEventArgs e)
        {
            if (IsSuppressed(e.BundleId)) e.State = RequestState.None;
        }

        private bool TryGetBrokenRelated(string bundleId, out RelatedBundleObservation related)
        {
            lock (this.detectionGate)
                return this.relatedBundles.TryGetValue(bundleId, out related) && LegacyBrokenBundlePolicy.ShouldSuppress(related);
        }

        private bool TryGetRelated(string bundleId, out RelatedBundleObservation related)
        {
            lock (this.detectionGate) return this.relatedBundles.TryGetValue(bundleId, out related);
        }

        private bool IsSuppressed(string bundleId)
        {
            lock (this.detectionGate) return this.suppressedBundles.Contains(bundleId);
        }

        private void OnPlanComplete(object sender, PlanCompleteEventArgs e)
        {
            this.Engine.Log(e.Status < 0 ? LogLevel.Error : LogLevel.Standard, "PLAN_COMPLETE result=" + FormatCode(e.Status));
            if (e.Status < 0)
            {
                this.result = e.Status;
                if (this.viewModel == null) Close(); else DispatchViewModel(vm => vm.SetFailure(e.Status));
                return;
            }
            var applyOwner = GetApplyOwnerHandle();
            this.Engine.Log(LogLevel.Standard, "APPLY_OWNER_WINDOW mode=" + this.execution.Mode.ToString().ToUpperInvariant() +
                " handle=" + FormatHandle(applyOwner) + " valid=true visible=" + (this.viewModel == null ? "false" : "true") +
                " source=" + (this.viewModel == null ? "hidden-native" : "wpf-window") + " process=current lifetime=through-shutdown");
            this.Engine.Apply(applyOwner);
        }

        private IntPtr GetApplyOwnerHandle()
        {
            if (this.viewModel != null)
            {
                if (this.viewModel.WindowHandle == IntPtr.Zero) throw new InvalidOperationException("INTERACTIVE_APPLY_OWNER_MISSING");
                return this.viewModel.WindowHandle;
            }

            if (this.headlessApplyOwner == null) this.headlessApplyOwner = ApplyOwnerWindow.Create();
            if (!this.headlessApplyOwner.IsValid || this.headlessApplyOwner.IsVisible ||
                !this.headlessApplyOwner.IsOwnedByCurrentProcess || !this.headlessApplyOwner.HasHeadlessStyles)
                throw new InvalidOperationException("HEADLESS_APPLY_OWNER_INVALID");
            return this.headlessApplyOwner.Handle;
        }

        private static string FormatHandle(IntPtr handle) => "0x" + handle.ToInt64().ToString("X");

        private void OnApplyBegin(object sender, ApplyBeginEventArgs e)
        {
            this.Engine.Log(LogLevel.Standard, "APPLY_BEGIN");
            DispatchViewModel(vm => vm.SetApplying(this.activeIntent));
        }

        private void OnExecutePackageBegin(object sender, ExecutePackageBeginEventArgs e)
        {
            e.Cancel = e.Cancel || IsCancelRequested;
            if (String.Equals(e.PackageId, CleanupPackageId, StringComparison.Ordinal))
                this.Engine.Log(LogLevel.Standard, "CLEANUP_LOG path=" + Sanitize(this.cleanupLogPath));
            DispatchViewModel(vm => vm.SetStage(String.Equals(e.PackageId, CleanupPackageId, StringComparison.Ordinal) ? "Completando actualización" : "Instalando componentes"));
        }

        private void OnExecuteProgress(object sender, ExecuteProgressEventArgs e)
        {
            e.Cancel = e.Cancel || IsCancelRequested;
            DispatchViewModel(vm => vm.SetProgress(e.OverallPercentage));
        }

        private void OnExecutePackageComplete(object sender, ExecutePackageCompleteEventArgs e)
        {
            if (String.Equals(e.PackageId, CleanupPackageId, StringComparison.Ordinal))
            {
                LogCleanupSummary();
                this.Engine.Log(e.Status < 0 ? LogLevel.Error : LogLevel.Standard,
                    "CLEANUP_EXIT result=" + FormatCode(e.Status) + " exitCode=" + NormalizeExitCode(e.Status) + " log=" + Sanitize(this.cleanupLogPath));
                if (e.Status < 0)
                {
                    this.cleanupPackageFailed = true;
                    this.Engine.Log(LogLevel.Error, "STALE_PREDECESSOR_CLEANUP_FAILED result=" + FormatCode(e.Status) + " log=" + Sanitize(this.cleanupLogPath));
                }
            }
            DispatchViewModel(vm => vm.SetStage("Finalizando"));
        }

        private void OnError(object sender, WixToolset.BootstrapperApplicationApi.ErrorEventArgs e)
        {
            this.result = e.ErrorCode;
            this.Engine.Log(LogLevel.Error, "BA_ERROR code=" + FormatCode(e.ErrorCode));
            e.Result = IsCancelRequested ? Result.Cancel : this.execution.Mode == BootstrapperMode.Interactive ? Result.Ok : Result.Error;
        }

        private void OnApplyComplete(object sender, ApplyCompleteEventArgs e)
        {
            this.result = e.Status;
            this.Engine.Log(e.Status < 0 ? LogLevel.Error : LogLevel.Standard, "APPLY_COMPLETE result=" + FormatCode(e.Status) + " restart=" + e.Restart);
            if (this.cleanupPackageFailed && e.Status >= 0) this.Engine.Log(LogLevel.Error, "UPDATE_INSTALLED_CLEANUP_PENDING");

            if (this.viewModel == null) { Close(); return; }
            Dispatch(() =>
            {
                if (e.Status >= 0 && this.cleanupPackageFailed) this.viewModel.SetCleanupPending();
                else if (e.Status >= 0 && e.Restart == ApplyRestart.RestartRequired) this.viewModel.SetRestartRequired();
                else if (e.Status >= 0) this.viewModel.SetSuccess();
                else this.viewModel.SetFailure(e.Status);
                if (this.viewModel.CloseWhenApplyCompletes) Close();
            });
        }

        internal void OpenLog()
        {
            try
            {
                var path = this.cleanupPackageFailed && !String.IsNullOrWhiteSpace(this.cleanupLogPath) && File.Exists(this.cleanupLogPath)
                    ? this.cleanupLogPath
                    : this.Engine.GetVariableString("WixBundleLog");
                if (!String.IsNullOrWhiteSpace(path) && File.Exists(path))
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "open" });
            }
            catch (Exception ex) { this.Engine.Log(LogLevel.Error, "OPEN_LOG_FAILED reason=" + Sanitize(ex.Message)); }
        }

        internal void Close()
        {
            if (this.window != null && this.window.IsVisible) this.window.CloseFromViewModel();
            else if (this.dispatcher != null && !this.dispatcher.HasShutdownStarted) this.dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        }

        internal void ShutdownDispatcher() => this.dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);

        private static string CreateCleanupLogPath()
        {
            var name = "legacy-bundle-cleanup-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss.fffZ") + "-p" + Process.GetCurrentProcess().Id + ".log";
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Galtek", "Classroom", "Installer", "logs", name);
        }

        private void LogCleanupSummary()
        {
            try
            {
                if (String.IsNullOrWhiteSpace(this.cleanupLogPath) || !File.Exists(this.cleanupLogPath) || new FileInfo(this.cleanupLogPath).Length > 262144) return;
                foreach (var raw in File.ReadAllLines(this.cleanupLogPath))
                {
                    var separator = raw.IndexOf(' ');
                    var message = separator >= 0 ? raw.Substring(separator + 1) : raw;
                    if (message.StartsWith("CLEANUP_BLOCKED", StringComparison.Ordinal) ||
                        message.StartsWith("CLEANUP_FAILED", StringComparison.Ordinal) ||
                        message.StartsWith("CLEANUP_COMPLETE", StringComparison.Ordinal) ||
                        (message.StartsWith("CLEANUP_PRECONDITION", StringComparison.Ordinal) && message.Contains(" result=FAIL")))
                        this.Engine.Log(message.StartsWith("CLEANUP_COMPLETE", StringComparison.Ordinal) ? LogLevel.Standard : LogLevel.Error, "CLEANUP_HELPER " + Sanitize(message));
                }
            }
            catch (Exception ex)
            {
                this.Engine.Log(LogLevel.Error, "CLEANUP_LOG_READ_FAILED exception=" + ex.GetType().Name + " path=" + Sanitize(this.cleanupLogPath));
            }
        }

        private void DispatchViewModel(Action<InstallerViewModel> action)
        {
            if (this.viewModel != null) Dispatch(() => action(this.viewModel));
        }

        private void Dispatch(Action action)
        {
            if (this.dispatcher == null || this.dispatcher.HasShutdownStarted) return;
            this.dispatcher.BeginInvoke(action);
        }

        private static InstallerIntent IntentFor(LaunchAction action)
        {
            if (action == LaunchAction.Uninstall || action == LaunchAction.UnsafeUninstall) return InstallerIntent.Uninstall;
            if (action == LaunchAction.Repair) return InstallerIntent.Repair;
            return InstallerIntent.Install;
        }

        private static int CompareVersions(string left, string right)
        {
            if (!Version.TryParse(left, out var l) || !Version.TryParse(right, out var r)) return 0;
            return l.CompareTo(r);
        }

        internal static string FormatCode(int value) => "0x" + unchecked((uint)value).ToString("X8");
        private static string Sanitize(string value) => (value ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        private static string StateLogName(ProductState state)
        {
            switch (state)
            {
                case ProductState.LegacySupported: return "LEGACY_SUPPORTED";
                case ProductState.InstalledSame: return "INSTALLED_SAME";
                case ProductState.InstalledOlder: return "INSTALLED_OLDER";
                case ProductState.InstalledNewer: return "INSTALLED_NEWER";
                case ProductState.RepairablePartial: return "REPAIRABLE_PARTIAL";
                case ProductState.BlockedConflict: return "BLOCKED_CONFLICT";
                case ProductState.DetectionFailed: return "DETECTION_FAILED";
                case ProductState.CleanupPending: return "CLEANUP_PENDING";
                case ProductState.RestartRequired: return "RESTART_REQUIRED";
                default: return state.ToString().ToUpperInvariant();
            }
        }
        private static int NormalizeExitCode(int value) => (value & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) ? value & 0xFFFF : value;
    }
}
