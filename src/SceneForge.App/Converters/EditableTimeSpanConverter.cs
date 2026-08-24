using System.Globalization;
using System.Windows.Data;

namespace SceneForge.App.Converters;

// Two-way TimeSpan <-> string for Scene Review's boundary-adjustment text
// boxes. Accepts "h:mm:ss[.f]", "m:ss[.f]", or plain seconds ("12.5") on the
// way back in; an unparseable edit is rejected via Binding.DoNothing (the
// text box keeps the user's in-progress text and the bound TimeSpan is left
// unchanged) rather than throwing or silently coercing to zero.
[ValueConversion(typeof(TimeSpan), typeof(string))]
public sealed class EditableTimeSpanConverter : IValueConverter
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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return Binding.DoNothing;
        }

        var parts = text.Trim().Split(':');
        try
        {
            return parts.Length switch
            {
                1 => TimeSpan.FromSeconds(double.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture)),
                2 => TimeSpan.FromMinutes(int.Parse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture))
                    + TimeSpan.FromSeconds(double.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture)),
                3 => TimeSpan.FromHours(int.Parse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture))
                    + TimeSpan.FromMinutes(int.Parse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture))
                    + TimeSpan.FromSeconds(double.Parse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture)),
                _ => Binding.DoNothing,
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return Binding.DoNothing;
        }
    }
}
