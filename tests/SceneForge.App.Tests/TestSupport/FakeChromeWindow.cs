using System.Windows;
using SceneForge.App.ViewModels;

namespace SceneForge.App.Tests.TestSupport;

// In-memory stand-in for the shell Window so WindowChromeViewModel's caption
// commands can be exercised without an STA message loop / real Window.
public sealed class FakeChromeWindow : IChromeWindow
{
    public WindowState WindowState { get; set; } = WindowState.Normal;

    public int CloseCallCount { get; private set; }

    public void Close() => CloseCallCount++;
}
