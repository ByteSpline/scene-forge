namespace SceneForge.Media.Rendering;

// One self-correction the batched render strategy made: a batch of
// SegmentCount segments failed with what ffmpeg reported as an
// out-of-memory / allocation error, so FFmpegRenderService automatically
// re-rendered it as two smaller batches (FirstHalfSegmentCount +
// SecondHalfSegmentCount) rather than failing the whole render. Recorded on
// RenderResult.BatchSplitEvents (and written to System.Diagnostics.Trace as
// it happens) so the adaptive batch-sizing behavior is visible and
// debuggable - the render never needs to know in advance how many segments
// is "too many" for a given machine; it discovers the working size here.
public sealed record RenderBatchSplitEvent
{
    // How many segments the batch that failed contained.
    public required int SegmentCount { get; init; }

    // The two sub-batches it was split into (they sum to SegmentCount).
    public required int FirstHalfSegmentCount { get; init; }

    public required int SecondHalfSegmentCount { get; init; }

    // 0 for a top-level batch, 1 for a half, 2 for a quarter, ... - how many
    // times this run of segments has been halved so far.
    public required int Depth { get; init; }

    // A short excerpt of the ffmpeg stderr that triggered the split, kept so
    // an operator can confirm it really was a memory failure and not
    // something else that happened to recover at a smaller size.
    public required string FfmpegErrorExcerpt { get; init; }
}
