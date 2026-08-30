using System.Windows;
using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;

namespace SceneForge.App.Tests.ViewModels;

// The custom title bar's minimize / maximize-restore / close buttons are
// bound to these RelayCommands (MainWindow.xaml), not to code-behind Click
// handlers - so a passing suite here is what confirms the window controls
// are present and wired to real, working commands.
public class WindowChromeViewModelTests
{
    [Fact]
    public void MinimizeCommand_SetsWindowStateToMinimized()
    {
        var window = new FakeChromeWindow { WindowState = WindowState.Normal };
        var vm = new WindowChromeViewModel(window);

        vm.MinimizeCommand.Execute(null);

        Assert.Equal(WindowState.Minimized, window.WindowState);
    }

    [Fact]
    public void ToggleMaximizeCommand_FromNormal_Maximizes()
    {
        var window = new FakeChromeWindow { WindowState = WindowState.Normal };
        var vm = new WindowChromeViewModel(window);

        vm.ToggleMaximizeCommand.Execute(null);

        Assert.Equal(WindowState.Maximized, window.WindowState);
    }

    [Fact]
    public void ToggleMaximizeCommand_FromMaximized_Restores()
    {
        var window = new FakeChromeWindow { WindowState = WindowState.Maximized };
        var vm = new WindowChromeViewModel(window);

        vm.ToggleMaximizeCommand.Execute(null);

        Assert.Equal(WindowState.Normal, window.WindowState);
    }

    [Fact]
    public void CloseCommand_ClosesTheWindow()
    {
        var window = new FakeChromeWindow();
        var vm = new WindowChromeViewModel(window);

        vm.CloseCommand.Execute(null);

        Assert.Equal(1, window.CloseCallCount);
    }

    [Fact]
    public void IsMaximizedAndLabel_TrackWindowState()
    {
        var window = new FakeChromeWindow { WindowState = WindowState.Normal };
        var vm = new WindowChromeViewModel(window);

        Assert.False(vm.IsMaximized);
        Assert.Equal("Maximize window", vm.MaximizeRestoreLabel);

        window.WindowState = WindowState.Maximized;
        vm.NotifyWindowStateChanged();

        Assert.True(vm.IsMaximized);
        Assert.Equal("Restore window", vm.MaximizeRestoreLabel);
    }

    [Fact]
    public void NotifyWindowStateChanged_RaisesPropertyChangedForCaptionState()
    {
        var window = new FakeChromeWindow { WindowState = WindowState.Normal };
        var vm = new WindowChromeViewModel(window);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        window.WindowState = WindowState.Maximized;
        vm.NotifyWindowStateChanged();

        Assert.Contains(nameof(WindowChromeViewModel.IsMaximized), changed);
        Assert.Contains(nameof(WindowChromeViewModel.MaximizeRestoreLabel), changed);
    }
}
