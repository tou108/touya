using System;
using System.Windows;
using System.Windows.Threading;
using NXMacroAdvanced.ViewModels;

namespace NXMacroAdvanced
{
    public partial class App : Application
    {
        private MainViewModel? _mainViewModel;

        protected override void OnStartup(StartupEventArgs e)
        {
            // ── グローバル例外ハンドラー（黙ってクラッシュしないようにする）──
            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
                ShowFatal(ex.ExceptionObject?.ToString() ?? "不明なエラー");

            DispatcherUnhandledException += (_, ex) =>
            {
                ShowFatal(ex.Exception.ToString());
                ex.Handled = true;
            };

            base.OnStartup(e);

            try
            {
                _mainViewModel = new MainViewModel();
                var window = new MainWindow { DataContext = _mainViewModel };
                window.Show();
            }
            catch (Exception ex)
            {
                ShowFatal(ex.ToString());
            }
        }

        private static void ShowFatal(string message)
        {
            MessageBox.Show(message, "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            Current?.Shutdown(1);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mainViewModel?.Dispose();
            base.OnExit(e);
        }
    }
}
