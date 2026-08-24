using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace SceneForge.App.Converters;

// Splits a PascalCase enum name into space-separated words for display -
// e.g. TransitionType.FadeToBlack -> "Fade To Black". Applies to any enum;
// used across Scene Review (TransitionType, RejectionReason), Export
// Settings (AspectFitMode), and Completion (VideoEncoderKind).
[ValueConversion(typeof(Enum), typeof(string))]
public sealed class EnumToSpacedStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Enum enumValue)
        {
            return string.Empty;
        }

        var name = enumValue.ToString();
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(name[i]);
        }

        return builder.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Spaced enum strings are display-only.");
}
