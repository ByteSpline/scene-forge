using System.Globalization;
using System.Windows.Data;

namespace SceneForge.App.Converters;

// Renders a 0..1 confidence/score value as a rounded percentage string, e.g.
// "82%". Deliberately whole-percent, not decimal, precision: CLAUDE.md rule
// 10 forbids presenting these heuristic scores as more exact than they are.
[ValueConversion(typeof(double), typeof(string))]
public sealed class ConfidenceToPercentStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double confidence
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(confidence * 100)}%")
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Percent strings are display-only.");
}
