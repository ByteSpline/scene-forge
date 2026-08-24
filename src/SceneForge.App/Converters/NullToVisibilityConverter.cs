using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SceneForge.App.Converters;

// Visible when the bound value is non-null (or, for a bool, true); Collapsed
// otherwise. Pass converter parameter "Invert" to flip the sense - one
// converter covers both "show only once a result exists" and "show only
// while nothing has completed yet" bindings across the progress screens.
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            bool boolValue => boolValue,
            string text => !string.IsNullOrEmpty(text),
            _ => true,
        };

        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        if (invert)
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Visibility conversion is one-way.");
}
