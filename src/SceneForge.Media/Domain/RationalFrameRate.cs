using System.Globalization;

namespace SceneForge.Media.Domain;

public readonly record struct RationalFrameRate
{
    public long Numerator { get; }

    public long Denominator { get; }

    public RationalFrameRate(long numerator, long denominator)
    {
        if (denominator < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "Denominator must not be negative.");
        }

        if (denominator == 0 && numerator != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "A zero denominator only represents an undefined frame rate and requires a zero numerator.");
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    public static RationalFrameRate Undefined => new(0, 0);

    public bool IsDefined => Denominator != 0;

    public double? ToDouble() => IsDefined ? (double)Numerator / Denominator : null;

    public static RationalFrameRate Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Undefined;
        }

        var parts = value.Split('/', 2);
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator)
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator))
        {
            throw new FormatException($"'{value}' is not a valid rational frame rate in 'numerator/denominator' form.");
        }

        if (denominator == 0)
        {
            return Undefined;
        }

        return new RationalFrameRate(numerator, denominator);
    }

    public override string ToString() => IsDefined
        ? $"{Numerator}/{Denominator}"
        : "undefined";
}
