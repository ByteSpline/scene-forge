using System.Globalization;
using System.Windows.Data;
using SceneForge.App.Navigation;

namespace SceneForge.App.Converters;

// Renders the shell's step indicator, e.g. "Step 3 of 8 - Analysis
// progress" - a single always-visible, screen-reader-announceable text
// (AutomationProperties.LiveSetting="Polite" on its TextBlock) rather than a
// purely visual breadcrumb graphic, so keyboard/screen-reader users always
// know where they are in the eight-step workflow.
[ValueConversion(typeof(WorkflowStep), typeof(string))]
public sealed class WorkflowStepToDisplayConverter : IValueConverter
{
    private static readonly string[] Labels =
    [
        "Welcome & Import",
        "Analysis Settings",
        "Analysis Progress",
        "Scene Review",
        "Timeline Summary",
        "Export Settings",
        "Render Progress",
        "Completion",
    ];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not WorkflowStep step)
        {
            return string.Empty;
        }

        var index = (int)step;
        var label = index >= 0 && index < Labels.Length ? Labels[index] : step.ToString();
        return string.Create(CultureInfo.InvariantCulture, $"Step {index + 1} of {Labels.Length} — {label}");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Step display strings are display-only.");
}
