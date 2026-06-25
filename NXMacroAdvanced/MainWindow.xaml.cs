using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NXMacroAdvanced.ViewModels;

namespace NXMacroAdvanced
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Closed += (s, e) => (DataContext as MainViewModel)?.Dispose();
        }
    }
}
