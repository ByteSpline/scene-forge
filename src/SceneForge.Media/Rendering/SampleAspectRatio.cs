namespace SceneForge.Media.Rendering;

// The pixel (sample) aspect ratio ffmpeg's setsar filter should stamp onto
// every normalized segment - distinct from the frame's display aspect ratio
// (Width/Height), which RenderOutputSpec already fixes directly. Every clip
// is normalized to the same SampleAspectRatio before concatenation
// (CLAUDE.md-adjacent phase requirement: "normalize every selected segment
// ... before concatenation"), almost always Square (1/1) for the standard
// square-pixel outputs this renderer targets.
public readonly record struct SampleAspectRatio
{
    public long Numerator { get; }

    public long Denominator { get; }

    public SampleAspectRatio(long numerator, long denominator)
    {
        if (numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "Numerator must be positive.");
        }

        if (denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "Denominator must be positive.");
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    public static SampleAspectRatio Square { get; } = new(1, 1);

    // ffmpeg's setsar filter accepts either 'num/den' or 'num:den' - '/' is
    // used here for consistency with RationalFrameRate.ToString().
    public string ToFfmpegRatio() => $"{Numerator}/{Denominator}";

    public override string ToString() => ToFfmpegRatio();
}
