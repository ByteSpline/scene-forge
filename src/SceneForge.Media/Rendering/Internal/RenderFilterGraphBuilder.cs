using System.Globalization;
using System.Text;

namespace SceneForge.Media.Rendering.Internal;

// Builds the single ffmpeg filter_complex graph that trims every
// RenderSegment from input 0 (the original source file), normalizes each to
// RenderPlan.OutputSpec's exact resolution/pixel format/sample aspect
// ratio/frame rate, concatenates them in Position order, and separately
// trims/reformats input 1 (the supplied audio file) to match - one filter
// graph, one encode pass, per the phase brief's "prefer a single final
// encoding pass". Never referenced as [0:a] anywhere, which is what
// structurally removes the source audio (see FFmpegRenderService).
internal static class RenderFilterGraphBuilder
{
    public const string VideoOutputLabel = "[vout]";
    public const string AudioOutputLabel = "[aout]";

    public static string Build(RenderPlan plan)
    {
        var spec = plan.OutputSpec;
        var segmentParts = new List<string>(plan.Segments.Count);
        var concatInputs = new StringBuilder();

        for (var i = 0; i < plan.Segments.Count; i++)
        {
            var segment = plan.Segments[i];
            var label = $"v{i}";
            segmentParts.Add(BuildSegmentFilter(segment, spec, plan.SourceRotationDegrees, label));
            concatInputs.Append('[').Append(label).Append(']');
        }

        var concat = $"{concatInputs}concat=n={plan.Segments.Count}:v=1:a=0{VideoOutputLabel}";
        var audio = BuildAudioFilter(plan.Audio);

        segmentParts.Add(concat);
        segmentParts.Add(audio);
        return string.Join(';', segmentParts);
    }

    private static string BuildSegmentFilter(RenderSegment segment, RenderOutputSpec spec, int rotationDegrees, string label)
    {
        var start = FormatSeconds(segment.SourceStart);
        var duration = FormatSeconds(segment.SourceDuration);

        var builder = new StringBuilder();
        builder.Append("[0:v]trim=start=").Append(start).Append(":duration=").Append(duration)
            .Append(",setpts=PTS-STARTPTS");

        AppendRotationFilters(builder, rotationDegrees);
        AppendFitFilters(builder, spec);

        builder.Append(",fps=").Append(spec.FrameRate.Numerator).Append('/').Append(spec.FrameRate.Denominator)
            .Append(",format=").Append(spec.PixelFormat)
            .Append(",setsar=").Append(spec.SampleAspectRatio.ToFfmpegRatio())
            .Append('[').Append(label).Append(']');

        return builder.ToString();
    }

    // ffprobe's rotation convention (see VideoStreamInfo.RotationDegrees,
    // normalized into [0, 360)): a positive value describes the clockwise
    // rotation the player is expected to apply to display the frame
    // upright. transpose=1 (90 clockwise) / transpose=2 (90
    // counter-clockwise, i.e. the visual result of undoing a 270-clockwise
    // source rotation) is ffmpeg's own filter convention.
    private static void AppendRotationFilters(StringBuilder builder, int rotationDegrees)
    {
        switch (((rotationDegrees % 360) + 360) % 360)
        {
            case 90:
                builder.Append(",transpose=1");
                break;
            case 180:
                builder.Append(",hflip,vflip");
                break;
            case 270:
                builder.Append(",transpose=2");
                break;
        }
    }

    private static void AppendFitFilters(StringBuilder builder, RenderOutputSpec spec)
    {
        var w = spec.Width.ToString(CultureInfo.InvariantCulture);
        var h = spec.Height.ToString(CultureInfo.InvariantCulture);

        switch (spec.FitMode)
        {
            case AspectFitMode.Letterbox:
                builder.Append(",scale=").Append(w).Append(':').Append(h)
                    .Append(":force_original_aspect_ratio=decrease:flags=bicubic")
                    .Append(",pad=").Append(w).Append(':').Append(h).Append(":(ow-iw)/2:(oh-ih)/2:color=").Append(spec.PadColor);
                break;
            case AspectFitMode.Fill:
                builder.Append(",scale=").Append(w).Append(':').Append(h)
                    .Append(":force_original_aspect_ratio=increase:flags=bicubic")
                    .Append(",crop=").Append(w).Append(':').Append(h);
                break;
            case AspectFitMode.Stretch:
                builder.Append(",scale=").Append(w).Append(':').Append(h).Append(":flags=bicubic");
                break;
            default:
                throw new RenderPlanException($"Unsupported AspectFitMode '{spec.FitMode}'.");
        }
    }

    private static string BuildAudioFilter(RenderAudioTrackSpec audio) =>
        $"[1:a]atrim=start={FormatSeconds(audio.TrimStart)}:duration={FormatSeconds(audio.TrimDuration)},asetpts=PTS-STARTPTS,aformat=sample_rates={audio.SampleRateHz.ToString(CultureInfo.InvariantCulture)}{AudioOutputLabel}";

    private static string FormatSeconds(TimeSpan value) => value.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture);
}
