using System.ComponentModel;
using System.Windows;

namespace GaltekClassroom.Bootstrapper
{
    public partial class MainWindow : Window
    {
        private readonly InstallerViewModel viewModel;
        private bool closeFromViewModel;

        public MainWindow(InstallerViewModel viewModel)
        {
            this.viewModel = viewModel;
            this.DataContext = viewModel;
            InitializeComponent();
            Loaded += (s, e) => ((GaltekBootstrapperApplication)System.Windows.Application.Current?.Properties["Bootstrapper"])?.Engine.CloseSplashScreen();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!this.closeFromViewModel && !this.viewModel.RequestWindowClose(this)) e.Cancel = true;
            base.OnClosing(e);
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            ((GaltekBootstrapperApplication)Application.Current.Properties["Bootstrapper"]).ShutdownDispatcher();
        }

        public void CloseFromViewModel()
        {
            this.closeFromViewModel = true;
            Close();
        }
    }
}
