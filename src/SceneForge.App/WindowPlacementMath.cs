using System.Windows;

namespace SceneForge.App;

// Pure geometry for placing the shell window. Extracted from
// MainWindow.xaml.cs so the "never let the custom title bar get clipped off
// the top of the screen" rule is covered by a test (see
// WindowPlacementMathTests) rather than only by manually launching the app
// on a short display.
internal static class WindowPlacementMath
{
    // Shrinks (never grows) the requested size to fit inside workArea, then
    // centres it there. The returned Left/Top are always within workArea, so
    // the window's top edge - and the custom caption drawn there - is never
    // above workArea.Top.
    public static Placement CentreWithin(double requestedWidth, double requestedHeight, Rect workArea)
    {
        var width = Clamp(requestedWidth, 1, workArea.Width);
        var height = Clamp(requestedHeight, 1, workArea.Height);
        var left = workArea.Left + ((workArea.Width - width) / 2);
        var top = workArea.Top + ((workArea.Height - height) / 2);
        return new Placement(width, height, left, top);
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : (value > max ? max : value);

    public readonly record struct Placement(double Width, double Height, double Left, double Top);
}
