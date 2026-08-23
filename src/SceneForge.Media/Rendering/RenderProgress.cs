namespace SceneForge.Media.Rendering;

// One machine-readable progress update, parsed from ffmpeg's own
// '-progress pipe:1' key=value stream (Internal.RenderProgressParser) -
// never scraped from ffmpeg's human-oriented banner/stats text, which is
// not a stable format across builds.
public sealed record RenderProgress
{
    public required long FrameNumber { get; init; }

    public double? FramesPerSecond { get; init; }

    // Position within the OUTPUT timeline ffmpeg has encoded up to so far.
    public required TimeSpan OutTime { get; init; }

    // Encoding speed as a multiple of realtime (2.0 = twice as fast as
    // realtime playback); null when ffmpeg has not reported one yet.
    public double? Speed { get; init; }

    public required TimeSpan Elapsed { get; init; }

    // (PlannedVideoDuration - OutTime) / Speed - null whenever Speed is
    // null or not yet positive, since a divide-by-zero/negative ETA would
    // be a fabricated number, not a real estimate (CLAUDE.md rule 10).
    public TimeSpan? EstimatedTimeRemaining { get; init; }

    // True on the final update (ffmpeg reported 'progress=end').
    public required bool IsFinished { get; init; }
}
