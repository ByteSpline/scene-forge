namespace SceneForge.Media.Rendering;

// One TimelinePlacement translated into a source-media trim instruction.
// Deliberately carries only what FFmpegRenderService needs to cut this
// segment from the ORIGINAL source file - SourceStart/SourceDuration are
// TimelinePlacement.SourceRange.Start/UsedDuration verbatim, themselves
// full-resolution source timestamps (never analysis-proxy timestamps - see
// docs/PHASE_07_REPORT.md/PHASE_08_REPORT.md: CleanClip.Range and everything
// derived from it was always measured against the original file's own
// timeline, even though scoring read frames from a downscaled analysis
// stream).
public sealed record RenderSegment
{
    // TimelinePlacement.Position - Segments is always sorted by this, same
    // ordering the concat filter graph is built in.
    public required int Position { get; init; }

    public required TimeSpan SourceStart { get; init; }

    public required TimeSpan SourceDuration { get; init; }

    public required bool IsTrimmed { get; init; }
}
