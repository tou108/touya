using System.Windows;
using NXMacroAdvanced.ViewModels;

namespace NXMacroAdvanced
{
    public partial class App : Application
    {
        private MainViewModel? _mainViewModel;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _mainViewModel = new MainViewModel();
            var window = new MainWindow { DataContext = _mainViewModel };
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mainViewModel?.Dispose();
            base.OnExit(e);
        }
    }
}
