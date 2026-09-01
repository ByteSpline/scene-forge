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
        var video = BuildVideoConcat(plan.Segments, plan.OutputSpec, plan.SourceRotationDegrees);
        var audio = BuildAudioFilter(plan.Audio);
        return $"{video};{audio}";
    }

    // The video half only: trims every segment from input 0, normalizes each
    // to spec, and concatenates them in order, emitting VideoOutputLabel -
    // with no audio stage. FFmpegRenderService's batched render strategy
    // builds one of these per BOUNDED-SIZE batch of segments (so the
    // filtergraph node count stays small regardless of how many total
    // segments the plan has) and then joins the batch outputs with the
    // concat demuxer. Identical per-segment filter chain to Build.
    public static string BuildVideoConcat(
        IReadOnlyList<RenderSegment> segments,
        RenderOutputSpec spec,
        int rotationDegrees)
    {
        if (segments.Count == 1)
        {
            return BuildSegmentFilter(segments[0], spec, rotationDegrees, VideoOutputLabel.Trim('[', ']'));
        }

        var segmentParts = new List<string>(segments.Count + 1);
        var concatInputs = new StringBuilder();

        for (var i = 0; i < segments.Count; i++)
        {
            var label = $"v{i}";
            segmentParts.Add(BuildSegmentFilter(segments[i], spec, rotationDegrees, label));
            concatInputs.Append('[').Append(label).Append(']');
        }

        segmentParts.Add($"{concatInputs}concat=n={segments.Count}:v=1:a=0{VideoOutputLabel}");
        return string.Join(';', segmentParts);
    }

    // The audio half of the graph on its own, emitting AudioOutputLabel -
    // reused verbatim by the concat-demuxer strategy's final mux pass, where
    // the video is stream-copied from the pre-rendered segments and only the
    // audio still needs the trim/reformat chain.
    public static string BuildAudioOnlyGraph(RenderAudioTrackSpec audio) => BuildAudioFilter(audio);

    // The video half for FFmpegRenderService's concat-demuxer batch
    // strategy, where each of the batch's segments is fed by its OWN ffmpeg
    // input ('-ss <SourceStart> -i <source>', once per segment) instead of
    // all of them sharing input 0 through a split. The input-level seek
    // means ffmpeg decodes ~one GOP into each segment rather than the whole
    // source from frame 0 for every batch - the dominant cost when a plan's
    // segments are scattered across a long source. Segment k reads from
    // input k and trims from 0 (the '-ss' already positioned it);
    // SourceDuration still bounds the window and FFmpegRenderService pins
    // the exact concatenated frame count with '-frames:v', so the rendered
    // output is frame-identical to the shared-input form - only the input
    // label and the trim start differ. The per-segment normalization chain
    // and the concat node are otherwise byte-identical to BuildVideoConcat.
    public static string BuildSeekedVideoConcat(
        IReadOnlyList<RenderSegment> segments,
        RenderOutputSpec spec,
        int rotationDegrees)
    {
        if (segments.Count == 1)
        {
            return BuildSegmentFilter(segments[0], spec, rotationDegrees, VideoOutputLabel.Trim('[', ']'), inputIndex: 0, afterInputSeek: true);
        }

        var segmentParts = new List<string>(segments.Count + 1);
        var concatInputs = new StringBuilder();

        for (var i = 0; i < segments.Count; i++)
        {
            var label = $"v{i}";
            segmentParts.Add(BuildSegmentFilter(segments[i], spec, rotationDegrees, label, inputIndex: i, afterInputSeek: true));
            concatInputs.Append('[').Append(label).Append(']');
        }

        segmentParts.Add($"{concatInputs}concat=n={segments.Count}:v=1:a=0{VideoOutputLabel}");
        return string.Join(';', segmentParts);
    }

    private static string BuildSegmentFilter(
        RenderSegment segment,
        RenderOutputSpec spec,
        int rotationDegrees,
        string label,
        int inputIndex = 0,
        bool afterInputSeek = false)
    {
        // After an input-level '-ss <SourceStart>' seek the first frame the
        // filter graph sees is already the segment's start, so trim from 0;
        // without one, trim from the absolute source timestamp.
        var start = afterInputSeek ? "0" : FormatSeconds(segment.SourceStart);
        var duration = FormatSeconds(segment.SourceDuration);

        var builder = new StringBuilder();
        builder.Append('[').Append(inputIndex.ToString(CultureInfo.InvariantCulture)).Append(":v]trim=start=").Append(start).Append(":duration=").Append(duration)
            .Append(",setpts=PTS-STARTPTS");

        AppendRotationFilters(builder, rotationDegrees);
        AppendFitFilters(builder, spec);

        // ffmpeg's fps filter (the frame-RATE conversion below, needed
        // whenever a segment's source footage isn't already at
        // spec.FrameRate) duplicates/drops frames to hit the target rate
        // based on presentation timestamps, not on the trim window's exact
        // requested duration - when the source's native rate differs from
        // spec.FrameRate it can emit one or more frames MORE than
        // spec.FrameRate.ToFrameCount(segment.SourceDuration) actually
        // calls for, because it keeps extending/duplicating the last
        // source frame until it is told to stop, and a preceding
        // (time-domain) trim's 'duration=' does not give it an exact
        // downstream frame-count bound to stop at. Verified directly
        // against real ffmpeg 9.0.1 (a 30fps source trimmed to a
        // frame-exact-at-25fps duration and converted with fps=25/1 alone
        // produced a consistent, deterministic +1 frame per segment,
        // every segment, regardless of trim start offset - not a rounding
        // fluke - and the same excess compounds across a many-segment
        // concat exactly like the already-fixed trim-only case this
        // segment's own duration quantization addresses: 60 segments at
        // 30fps source / 25fps output measured 488 actual frames against
        // 470 expected). This is a source/output-frame-rate MISMATCH bug,
        // distinct from (and not fixed by) that per-segment duration
        // quantization: RenderPlanBuilder already guarantees
        // segment.SourceDuration is an exact multiple of spec.FrameRate's
        // frame period, but the fps filter's own frame count for that
        // exact duration is not reliably bound to the exact frame count
        // the duration implies whenever the source's rate differs. A
        // second, FRAME-domain trim right after fps= (start_frame/end_frame
        // count actual output frames rather than reading PTS, so it is
        // immune to the same boundary ambiguity) forces the segment back
        // to exactly the intended frame count regardless of what the fps
        // filter itself produced - verified to reduce that same 60-segment
        // case to 470/470 with zero delta, and a no-op (by construction)
        // whenever fps= was already exact, e.g. every fixture used
        // elsewhere in this test suite, all of which happen to already be
        // at the chosen output frame rate.
        var frameCount = spec.FrameRate.ToFrameCount(segment.SourceDuration);

        builder.Append(",fps=").Append(spec.FrameRate.Numerator).Append('/').Append(spec.FrameRate.Denominator)
            .Append(",trim=start_frame=0:end_frame=").Append(frameCount.ToString(CultureInfo.InvariantCulture))
            .Append(",setpts=PTS-STARTPTS")
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
