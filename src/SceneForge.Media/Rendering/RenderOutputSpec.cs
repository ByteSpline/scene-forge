using SceneForge.Media.Domain;

namespace SceneForge.Media.Rendering;

// Every selected segment is normalized to exactly this geometry, pixel
// format, sample aspect ratio, frame rate, and time base before
// concatenation - the phase brief's literal requirement - so the concat
// filter graph never has to reconcile mismatched inputs itself (ffmpeg's
// concat filter requires every input to already share format/dimensions/
// timebase; it does not normalize for you).
public sealed record RenderOutputSpec
{
    private readonly int _width = 1920;
    private readonly int _height = 1080;

    // Both dimensions must be positive and even - yuv420p (the default
    // PixelFormat) subsamples chroma 2x2 and cannot represent an odd
    // dimension.
    public int Width
    {
        get => _width;
        init => _width = ValidateDimension(value, nameof(Width));
    }

    public int Height
    {
        get => _height;
        init => _height = ValidateDimension(value, nameof(Height));
    }

    // The frame rate AND time base every segment is resampled to (via
    // ffmpeg's fps filter) - must match the RationalFrameRate the caller
    // passed as TimelinePlanRequest.OutputTimeBase when the TimelinePlan
    // being rendered was produced, or "matches the audio duration exactly
    // in the selected output time base" (Phase 8's own claim) stops being
    // true once frames are actually re-timed for output. RenderPlanBuilder
    // cannot verify this itself - TimelinePlan does not carry OutputTimeBase
    // back out - so this is a documented caller invariant, the same kind
    // TimelinePlanRequest.AvailableClips already is.
    public required RationalFrameRate FrameRate { get; init; }

    // ffmpeg pixel format name (e.g. "yuv420p") every segment is converted
    // to via the format filter before concatenation.
    public string PixelFormat { get; init; } = "yuv420p";

    public SampleAspectRatio SampleAspectRatio { get; init; } = SampleAspectRatio.Square;

    public AspectFitMode FitMode { get; init; } = AspectFitMode.Letterbox;

    // ffmpeg color spec used to pad letterboxed/pillarboxed content. Only
    // consulted when FitMode is Letterbox.
    public string PadColor { get; init; } = "black";

    private static int ValidateDimension(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Dimension must be positive.");
        }

        if (value % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Dimension must be even (required by 4:2:0-family pixel formats).");
        }

        return value;
    }
}
