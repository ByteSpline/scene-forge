using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SceneForge.App.ViewModels;

// The minimal window-management surface the custom title bar needs. The
// shell Window implements this directly (it already has WindowState/Close);
// keeping it behind an interface lets WindowChromeViewModel - and its
// commands - be unit-tested without constructing a real Window (which would
// require an STA message loop).
public interface IChromeWindow
{
    WindowState WindowState { get; set; }

    void Close();
}

// Backs the three custom caption buttons (minimize, maximize/restore,
// close) on MainWindow's title bar. This is pure view-layer window
// management - it drives no workflow state and touches nothing in
// SceneForge.Media - but it is a ViewModel with real RelayCommands (not
// code-behind Click handlers) so the buttons stay declaratively bound and
// covered by tests (see WindowChromeViewModelTests).
public sealed partial class WindowChromeViewModel : ObservableObject
{
    private readonly IChromeWindow _window;

    public WindowChromeViewModel(IChromeWindow window)
    {
        _window = window;
    }

    public bool IsMaximized => _window.WindowState == WindowState.Maximized;

    // Drives which of the two maximize/restore caption buttons is shown and
    // its accessible name.
    public string MaximizeRestoreLabel => IsMaximized ? "Restore window" : "Maximize window";

    [RelayCommand]
    private void Minimize() => _window.WindowState = WindowState.Minimized;

    [RelayCommand]
    private void ToggleMaximize() =>
        _window.WindowState = IsMaximized ? WindowState.Normal : WindowState.Maximized;

    [RelayCommand]
    private void Close() => _window.Close();

    // Called by the shell Window whenever its WindowState changes so the
    // maximize/restore button swaps its icon and accessible name.
    public void NotifyWindowStateChanged()
    {
        OnPropertyChanged(nameof(IsMaximized));
        OnPropertyChanged(nameof(MaximizeRestoreLabel));
    }
}
