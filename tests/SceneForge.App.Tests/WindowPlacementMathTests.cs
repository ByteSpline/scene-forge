using System.Windows;
using SceneForge.App;

namespace SceneForge.App.Tests;

// Guards the fix for the custom-title-bar regression: with WindowStyle="None"
// there is no OS caption Windows will keep on-screen, so a shell window
// larger than the monitor work area (e.g. the 820px default height on a
// 1366x768 laptop) was being centred with a negative top - putting the
// entire 40px custom caption, and its min/max/close buttons, above the
// screen edge where the user could not see them.
public class WindowPlacementMathTests
{
    [Fact]
    public void CentreWithin_WindowTallerThanWorkArea_ClampsHeightAndKeepsTopOnScreen()
    {
        var work = new Rect(0, 0, 1366, 728);

        var placement = WindowPlacementMath.CentreWithin(1280, 820, work);

        Assert.Equal(728, placement.Height);
        Assert.Equal(1280, placement.Width);
        Assert.True(placement.Top >= work.Top, $"Top {placement.Top} is above the work area");
        Assert.True(placement.Left >= work.Left);
    }

    [Fact]
    public void CentreWithin_WindowFitsWorkArea_KeepsSizeAndCentres()
    {
        var work = new Rect(0, 0, 1920, 1040);

        var placement = WindowPlacementMath.CentreWithin(1280, 820, work);

        Assert.Equal(1280, placement.Width);
        Assert.Equal(820, placement.Height);
        Assert.Equal((1920 - 1280) / 2.0, placement.Left);
        Assert.Equal((1040 - 820) / 2.0, placement.Top);
    }

    [Fact]
    public void CentreWithin_OffsetWorkArea_PositionsRelativeToThatOrigin()
    {
        // e.g. a taskbar docked on the left, or a secondary monitor.
        var work = new Rect(120, 40, 1200, 700);

        var placement = WindowPlacementMath.CentreWithin(2000, 2000, work);

        Assert.Equal(1200, placement.Width);
        Assert.Equal(700, placement.Height);
        Assert.Equal(120, placement.Left);
        Assert.Equal(40, placement.Top);
    }
}
