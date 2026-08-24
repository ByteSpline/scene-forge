using System.Globalization;
using System.Windows.Data;

namespace SceneForge.App.Converters;

// Formats a TimeSpan as "h:mm:ss.f" (hours omitted when zero) for every
// timestamp/duration shown in the review, summary, and progress screens -
// one shared format so the same duration never renders differently on two
// screens.
[ValueConversion(typeof(TimeSpan), typeof(string))]
public sealed class TimeSpanToClockStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan timeSpan)
        {
            return string.Empty;
        }

        return timeSpan.Hours > 0
            ? timeSpan.ToString(@"h\:mm\:ss\.f", CultureInfo.InvariantCulture)
            : timeSpan.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Clock strings are display-only.");
}
