using System.Windows;
using System.Windows.Input;

namespace SceneForge.App.Behaviors;

// Attached-property drag-and-drop for the Welcome/Import drop zones. Pure
// UI-framework glue - it extracts the first dropped file path and forwards
// it to whichever ICommand the View bound (WelcomeImportViewModel's
// VideoImportCommand or AudioImportCommand); it never decides what a
// dropped file means, so no processing/validation logic lives in this
// class or in any code-behind (CLAUDE.md: UI stays separate from
// processing rules).
public static class DragDropImportBehavior
{
    public static readonly DependencyProperty DropCommandProperty = DependencyProperty.RegisterAttached(
        "DropCommand",
        typeof(ICommand),
        typeof(DragDropImportBehavior),
        new PropertyMetadata(null, OnDropCommandChanged));

    public static void SetDropCommand(DependencyObject element, ICommand? value) => element.SetValue(DropCommandProperty, value);

    public static ICommand? GetDropCommand(DependencyObject element) => (ICommand?)element.GetValue(DropCommandProperty);

    private static void OnDropCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.AllowDrop = true;
        element.PreviewDragOver += OnPreviewDragOver;
        element.PreviewDrop += OnPreviewDrop;
    }

    private static void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (sender is not DependencyObject element || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var command = GetDropCommand(element);
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var path = files.Length > 0 ? files[0] : null;

        if (command is not null && path is not null && command.CanExecute(path))
        {
            command.Execute(path);
        }

        e.Handled = true;
    }
}
