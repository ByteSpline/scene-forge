using System.Windows;
using SceneForge.App.ViewModels;

namespace SceneForge.App.Behaviors;

// Attached to the thumbnail Image in Scene Review's DataTemplate. WPF's
// virtualizing panel only raises FrameworkElement.Loaded for a row's
// visual container when that row is actually realized (scrolled into
// view, or reused via VirtualizingPanel.VirtualizationMode="Recycling") -
// wiring the thumbnail load to that event, rather than to the ViewModel's
// own construction, is what keeps thumbnail generation bounded to visible
// rows only (CLAUDE.md rule 6/7; see ClipReviewItemViewModel's remarks).
public static class LazyThumbnailLoader
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(LazyThumbnailLoader),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true)
        {
            return;
        }

        element.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipReviewItemViewModel item } && item.EnsureThumbnailLoadedCommand.CanExecute(null))
        {
            item.EnsureThumbnailLoadedCommand.Execute(null);
        }
    }
}
