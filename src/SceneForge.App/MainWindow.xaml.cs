using System.Windows;
using SceneForge.App.ViewModels;

namespace SceneForge.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
