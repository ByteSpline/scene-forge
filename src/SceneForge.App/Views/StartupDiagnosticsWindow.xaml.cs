using System.Windows;
using SceneForge.App.ViewModels;

namespace SceneForge.App.Views;

// Shown modally by App.OnStartup before MainWindow exists (see that
// method's remarks) - DialogResult communicates back whether the app should
// actually launch (true, only reachable once StartupDiagnosticsViewModel
// reports AllPassed) or shut down (false/null).
public partial class StartupDiagnosticsWindow : Window
{
    public StartupDiagnosticsWindow(StartupDiagnosticsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
