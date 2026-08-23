namespace SceneForge.Media.Rendering;

// Everything FFmpegRenderService needs to render one TimelinePlan to a
// concrete file: which source file to trim from (never an analysis proxy),
// the ordered list of trims, the shared normalization target every segment
// is converted to before concatenation, and the one audio track to mux in.
// Deliberately does not include an output path - CLAUDE.md rule 12 requires
// that to be a user-selected new path supplied at render time, not baked
// into a reusable plan.
public sealed record RenderPlan
{
    public required string SourceFilePath { get; init; }

    // Sorted by Position; never empty (RenderPlanBuilder rejects an empty
    // TimelinePlan).
    public required IReadOnlyList<RenderSegment> Segments { get; init; }

    public required RenderOutputSpec OutputSpec { get; init; }

    public required RenderAudioTrackSpec Audio { get; init; }

    // SceneForge.Media.Domain.VideoStreamInfo.RotationDegrees of the source
    // file's primary video stream, carried forward so FFmpegRenderService
    // can apply the matching transpose/flip filters before scaling - see
    // FfprobeService's own caveat that this value is ffprobe's reported
    // convention, not an independently verified physical rotation.
    public required int SourceRotationDegrees { get; init; }

    // TimelinePlan.PlannedDuration - the exact duration the concatenated,
    // normalized video is expected to have, used both to trim the audio
    // track to match and as the basis for output verification's duration
    // tolerance.
    public required TimeSpan PlannedVideoDuration { get; init; }
}
