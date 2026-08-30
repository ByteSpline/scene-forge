using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SceneForge.App.ViewModels;

namespace SceneForge.App;

// Shell window. Uses a custom (WindowStyle="None" + WindowChrome) title bar
// so the caption matches the app's own light/dark palette instead of the OS
// default chrome. Everything below is view-layer window management only - no
// workflow or media logic lives here (that stays in MainWindowViewModel and
// the step ViewModels). The caption buttons are bound to
// WindowChromeViewModel's RelayCommands, not Click handlers.
public partial class MainWindow : Window, IChromeWindow
{
    private readonly WindowChromeViewModel _chrome;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        _chrome = new WindowChromeViewModel(this);
        TitleBar.DataContext = _chrome;

        StateChanged += OnWindowStateChanged;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // The maximized-window clamp below keeps the frame inside the work
        // area, but WindowChrome still insets the client area by the resize
        // border when maximized - pad it back so the content is flush.
        WindowRootBorder.Padding = WindowState == WindowState.Maximized
            ? new Thickness(8)
            : new Thickness(0);

        _chrome.NotifyWindowStateChanged();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);

        FitIntoWorkArea();
    }

    // With WindowStyle="None" there is no OS caption for Windows to keep
    // on-screen, so a window taller/wider than the monitor's work area (or
    // one centred with a negative top on a short display) ends up with its
    // custom title bar clipped above the screen edge - exactly the "no
    // title bar, no buttons, blends in" symptom. Clamp the size to the work
    // area and centre the window inside it before the first frame is shown.
    // (WindowStartupLocation is Manual so nothing re-positions afterwards.)
    private void FitIntoWorkArea()
    {
        var placement = WindowPlacementMath.CentreWithin(Width, Height, SystemParameters.WorkArea);
        Width = placement.Width;
        Height = placement.Height;
        Left = placement.Left;
        Top = placement.Top;
    }

    // A borderless (WindowStyle="None") window maximizes to cover the
    // taskbar and spill a few pixels off every screen edge. Clamping the
    // maximized size and origin to the target monitor's work area in
    // WM_GETMINMAXINFO is the standard fix.
    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;

        if (msg == WM_GETMINMAXINFO)
        {
            try
            {
                AdjustMaximizedBounds(hwnd, lParam);
                handled = true;
            }
            catch (Exception)
            {
                // A WndProc hook must never let an exception escape into the
                // WPF message loop. On the (not-expected) failure path just
                // leave WM_GETMINMAXINFO unhandled - the window then
                // maximizes with the OS default bounds instead of the
                // work-area clamp, which is a cosmetic regression, not a
                // crash.
                handled = false;
            }
        }

        return IntPtr.Zero;
    }

    private static void AdjustMaximizedBounds(IntPtr hwnd, IntPtr lParam)
    {
        const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var work = monitorInfo.rcWork;
        var bounds = monitorInfo.rcMonitor;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition.x = work.left - bounds.left;
        mmi.ptMaxPosition.y = work.top - bounds.top;
        mmi.ptMaxSize.x = work.right - work.left;
        mmi.ptMaxSize.y = work.bottom - work.top;

        // fDeleteOld: false - lParam points at an OS-owned MINMAXINFO buffer
        // we are overwriting, not a block this process marshaled a managed
        // object into.
        Marshal.StructureToPtr(mmi, lParam, false);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

#pragma warning disable SA1307, SA1310 // native struct field names
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
#pragma warning restore SA1307, SA1310
}
