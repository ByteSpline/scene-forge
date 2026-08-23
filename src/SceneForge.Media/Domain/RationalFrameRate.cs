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

    // Rounds duration to the nearest whole frame count at this rate
    // (MidpointRounding.AwayFromZero) - the frame-accurate analogue of
    // "how many frames is this duration," used wherever a boundary must
    // land on an encodable/seekable cut point rather than an arbitrary tick
    // count (see Planning.TimelinePlanner, which quantizes a target audio
    // duration this way before planning against it).
    public long ToFrameCount(TimeSpan duration)
    {
        if (!IsDefined)
        {
            throw new InvalidOperationException("Cannot convert a duration to a frame count against an undefined frame rate.");
        }

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must not be negative.");
        }

        var frameCount = (decimal)duration.Ticks * Numerator / ((decimal)TimeSpan.TicksPerSecond * Denominator);
        return (long)Math.Round(frameCount, MidpointRounding.AwayFromZero);
    }

    // Inverse of ToFrameCount: the exact duration of frameCount whole frames
    // at this rate, rounded to the nearest TimeSpan tick (100ns) - most
    // rational frame rates, e.g. 30000/1001, do not divide TimeSpan's tick
    // resolution evenly, so this is the closest representable TimeSpan, not
    // a claim of infinite precision.
    public TimeSpan FromFrameCount(long frameCount)
    {
        if (!IsDefined)
        {
            throw new InvalidOperationException("Cannot convert a frame count to a duration against an undefined frame rate.");
        }

        if (frameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "Frame count must not be negative.");
        }

        var ticks = (decimal)frameCount * Denominator * TimeSpan.TicksPerSecond / Numerator;
        return TimeSpan.FromTicks((long)Math.Round(ticks, MidpointRounding.AwayFromZero));
    }

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
