using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SceneForge.App.Tests.Themes;

// Phase 17 gave the app-wide ListView style a full ControlTemplate (rounded
// container). CLAUDE.md rule 6/7 and the Phase 10 report both call Scene
// Review's ListView virtualization "real, not cosmetic" and load-bearing:
// a source with thousands of candidate clips must never realize thousands
// of row containers/thumbnails at once. This test instantiates a ListView
// with the real merged Styles.xaml, fills it with 3000 items inside a
// bounded-height host (the same shape SceneReviewView uses - a ListView in
// a Grid '*' row, no ancestor ScrollViewer), forces layout, and asserts
// only a screenful of containers were realized.
public class ListViewVirtualizationTests
{
    [Fact]
    public void AppListViewStyle_WithThousandsOfItems_RealizesOnlyAScreenful()
    {
        var (panelType, realizedChildren, nonNullContainers, isVirtualizing, canContentScroll) =
            RunOnStaThread(Probe);

        Assert.Equal(nameof(VirtualizingStackPanel), panelType);
        Assert.True(isVirtualizing, "VirtualizingPanel.IsVirtualizing is false on the retemplated ListView");
        Assert.True(canContentScroll, "ScrollViewer.CanContentScroll is false - the retemplated ScrollViewer scrolls by pixel over a fully-realized panel");
        Assert.InRange(realizedChildren, 1, 100);
        Assert.InRange(nonNullContainers, 1, 100);
    }

    private static (string PanelType, int RealizedChildren, int NonNullContainers, bool IsVirtualizing, bool CanContentScroll) Probe()
    {
        if (Application.Current is null)
        {
            _ = new Application();
        }

        foreach (var src in new[] { "Themes/Colors.Light.xaml", "Themes/Styles.xaml" })
        {
            Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/SceneForge.App;component/{src}"),
            });
        }

        var items = Enumerable.Range(0, 3000).Select(i => $"clip {i:0000}").ToArray();

        var list = new ListView { ItemsSource = items };
        list.SetResourceReference(FrameworkElement.StyleProperty, typeof(ListView));

        var host = new Grid { Width = 500, Height = 300 };
        host.Children.Add(list);

        var window = new Window
        {
            Content = host,
            Width = 520,
            Height = 340,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };

        try
        {
            window.Show();
            for (var i = 0; i < 6; i++)
            {
                PumpOnce();
            }

            var vsp = FindDescendant<VirtualizingStackPanel>(list);
            var panelType = vsp is not null
                ? nameof(VirtualizingStackPanel)
                : (FindDescendant<StackPanel>(list) is null ? "(none)" : nameof(StackPanel));
            var realized = vsp?.Children.Count
                ?? FindDescendant<StackPanel>(list)?.Children.Count
                ?? -1;

            var nonNull = Enumerable.Range(0, items.Length)
                .Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) is not null);

            return (
                panelType,
                realized,
                nonNull,
                VirtualizingPanel.GetIsVirtualizing(list),
                ScrollViewer.GetCanContentScroll(list));
        }
        finally
        {
            window.Close();
        }
    }

    private static void PumpOnce()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit)
            {
                return hit;
            }

            var deeper = FindDescendant<T>(child);
            if (deeper is not null)
            {
                return deeper;
            }
        }

        return null;
    }

    private static T RunOnStaThread<T>(Func<T> body)
    {
        T result = default!;
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw new InvalidOperationException("STA probe threw: " + captured, captured);
        }

        return result;
    }
}
