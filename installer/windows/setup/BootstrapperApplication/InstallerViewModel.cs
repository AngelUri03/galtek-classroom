using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WixToolset.BootstrapperApplicationApi;

namespace GaltekClassroom.Bootstrapper
{
    public sealed class InstallerViewModel : NotifyBase
    {
        private readonly GaltekBootstrapperApplication application;
        private readonly DispatcherTimer elapsedTimer;
        private readonly Stopwatch stopwatch = new Stopwatch();
        private ResolvedProductState resolved;
        private ProductState productState = ProductState.Detecting;
        private InstallerIntent activeIntent;
        private string stage = "Revisando el equipo";
        private int progress;
        private bool progressKnown;
        private string errorCode;

        public InstallerViewModel(GaltekBootstrapperApplication application, string bundleVersion)
        {
            this.application = application;
            this.BundleVersion = bundleVersion;
            this.PrimaryCommand = new RelayCommand(PrimaryAction, () => CanPrimary);
            this.SecondaryCommand = new RelayCommand(SecondaryAction, () => !IsBusy);
            this.DestructiveCommand = new RelayCommand(Uninstall, () => this.productState == ProductState.InstalledSame && !(this.resolved?.NeedsPredecessorCleanup ?? false));
            this.OpenLogCommand = new RelayCommand(application.OpenLog);
            this.elapsedTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            this.elapsedTimer.Tick += (s, e) => Changed(nameof(Elapsed));
            RefreshPresentation();
        }

        public IntPtr WindowHandle { get; set; }
        public string BundleVersion { get; }
        public ProductState ProductState => this.productState;
        public bool CancelRequested { get; private set; }
        public bool CloseWhenApplyCompletes { get; set; }
        public ICommand PrimaryCommand { get; }
        public ICommand SecondaryCommand { get; }
        public ICommand DestructiveCommand { get; }
        public ICommand OpenLogCommand { get; }
        public Presentation Presentation { get; private set; }
        public string Eyebrow => Presentation.Eyebrow;
        public string Title => Presentation.Title;
        public string Body => Presentation.Body;
        public string CardTitle => Presentation.CardTitle;
        public string CardBody => Presentation.CardBody;
        public string PrimaryLabel => Presentation.Primary;
        public string SecondaryLabel => Presentation.Secondary;
        public string DestructiveLabel => Presentation.Destructive;
        public Visibility PrimaryVisibility => String.IsNullOrWhiteSpace(PrimaryLabel) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SecondaryVisibility => String.IsNullOrWhiteSpace(SecondaryLabel) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility DestructiveVisibility => String.IsNullOrWhiteSpace(DestructiveLabel) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility LogVisibility => Presentation.ShowLog ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ProgressVisibility => Presentation.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ReadyVisibility => Presentation.ShowProgress ? Visibility.Collapsed : Visibility.Visible;
        public bool CanPrimary => Presentation.PrimaryEnabled && !IsBusy;
        public bool IsBusy => this.productState == ProductState.Planning || this.productState == ProductState.ApplyingInstall || this.productState == ProductState.ApplyingUpdate || this.productState == ProductState.ApplyingRepair || this.productState == ProductState.ApplyingUninstall;
        public int Progress => this.progress;
        public bool IsProgressIndeterminate => !this.progressKnown;
        public string CardAccent => this.productState == ProductState.Failure ? "#C83C3C" : "#FFA60C";
        public string Stage => this.stage;
        public string Elapsed => "Tiempo transcurrido  " + this.stopwatch.Elapsed.ToString(@"mm\:ss");
        public string Footer => "Versión " + BundleVersion + "  ·  GaltekSolution";

        public void SetResolvedState(ResolvedProductState value)
        {
            this.resolved = value;
            SetState(value.State);
        }

        public void SetPlanning(InstallerIntent intent)
        {
            this.activeIntent = intent;
            this.stage = "Calculando cambios";
            this.progressKnown = false;
            this.stopwatch.Restart();
            this.elapsedTimer.Start();
            SetState(ProductState.Planning);
        }

        public void SetApplying(InstallerIntent intent)
        {
            this.activeIntent = intent;
            var state = intent == InstallerIntent.Update || intent == InstallerIntent.CompleteUpdate ? ProductState.ApplyingUpdate :
                intent == InstallerIntent.Repair ? ProductState.ApplyingRepair :
                intent == InstallerIntent.Uninstall ? ProductState.ApplyingUninstall : ProductState.ApplyingInstall;
            SetState(state);
        }

        public void SetProgress(int value)
        {
            if (value < 0 || value > 100) return;
            // Burn commonly emits an initial zero before it has measurable
            // overall progress. Keep the active indeterminate state until a
            // real positive percentage arrives; never invent a percentage.
            if (!this.progressKnown && value == 0) return;
            this.progress = value;
            this.progressKnown = true;
            Changed(nameof(Progress));
            Changed(nameof(IsProgressIndeterminate));
        }

        public void SetStage(string value)
        {
            this.stage = value;
            Changed(nameof(Stage));
            RefreshPresentation();
        }

        public void SetSuccess()
        {
            StopTimer();
            SetState(ProductState.Success);
        }

        public void SetFailure(int code)
        {
            this.errorCode = GaltekBootstrapperApplication.FormatCode(code);
            StopTimer();
            SetState(ProductState.Failure);
        }

        public void SetRestartRequired()
        {
            StopTimer();
            SetState(ProductState.RestartRequired);
        }

        public void SetCleanupPending()
        {
            StopTimer();
            SetState(ProductState.CleanupPending);
        }

        public void BeginCommandLineAction(LaunchAction action)
        {
            if (action == LaunchAction.Uninstall) this.application.Plan(InstallerIntent.Uninstall);
            else if (action == LaunchAction.Repair) this.application.Plan(InstallerIntent.Repair);
            else if (action == LaunchAction.Install) this.application.Plan(ActionResolver.PrimaryIntent(this.productState));
        }

        public bool RequestWindowClose(Window owner)
        {
            if (!IsBusy) return true;
            var answer = MessageBox.Show(owner, "¿Cancelar la operación en curso?\n\nWindows intentará dejar el equipo en un estado seguro.", "Instalación de Galtek Classroom", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return false;
            this.CancelRequested = true;
            this.CloseWhenApplyCompletes = true;
            this.stage = "Cancelando de forma segura";
            Changed(nameof(Stage));
            return false;
        }

        private void PrimaryAction()
        {
            if (this.productState == ProductState.Success)
            {
                this.application.Close();
                return;
            }
            if (this.productState == ProductState.RestartRequired)
            {
                RestartWindows();
                return;
            }
            this.application.Plan(ActionResolver.PrimaryIntent(this.resolved));
        }

        private void SecondaryAction()
        {
            this.application.Close();
        }

        private void Uninstall()
        {
            var result = MessageBox.Show("Se quitarán los componentes de Galtek Classroom de este equipo.\nLos datos locales del producto se conservarán.", "¿Desinstalar Galtek Classroom?", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (result == MessageBoxResult.OK) this.application.Plan(InstallerIntent.Uninstall);
        }

        private void RestartWindows()
        {
            try
            {
                var shutdown = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
                Process.Start(new ProcessStartInfo { FileName = shutdown, Arguments = "/r /t 0 /d p:2:4", UseShellExecute = false, CreateNoWindow = true });
                this.application.Close();
            }
            catch
            {
                MessageBox.Show("No se pudo iniciar el reinicio. Reinicia Windows manualmente para completar la operación.", "Galtek Classroom", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SetState(ProductState value)
        {
            this.productState = value;
            RefreshPresentation();
            Changed(nameof(ProductState));
            Changed(nameof(CardAccent));
            Changed(nameof(IsBusy));
            Changed(nameof(CanPrimary));
            CommandManager.InvalidateRequerySuggested();
        }

        private void RefreshPresentation()
        {
            this.Presentation = PresentationResolver.Resolve(this.productState, this.activeIntent,
                this.resolved?.InstalledVersion, this.resolved?.AvailableVersion ?? BundleVersion,
                this.resolved?.Component, this.resolved?.Reason, this.errorCode, this.stage,
                this.resolved?.NeedsPredecessorCleanup ?? false);
            foreach (var property in new[] { nameof(Presentation), nameof(Eyebrow), nameof(Title), nameof(Body), nameof(CardTitle), nameof(CardBody), nameof(PrimaryLabel), nameof(SecondaryLabel), nameof(DestructiveLabel), nameof(PrimaryVisibility), nameof(SecondaryVisibility), nameof(DestructiveVisibility), nameof(LogVisibility), nameof(ProgressVisibility), nameof(ReadyVisibility), nameof(CanPrimary) }) Changed(property);
        }

        private void StopTimer()
        {
            this.stopwatch.Stop();
            this.elapsedTimer.Stop();
            Changed(nameof(Elapsed));
        }
    }
}
