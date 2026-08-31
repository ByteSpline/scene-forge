using System.Runtime.CompilerServices;
using OpenCvSharp;
using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Extraction.Signals;

// Streams one ClipFrameMetrics per sampled frame - the Extraction analogue
// of Detection.Signals.SignalPipeline. At most two AnalyzedFrame instances
// (previous, current) are ever alive at once, each disposed as soon as it
// is no longer needed, so memory stays flat regardless of video length
// (CLAUDE.md rule 6/7). Decoupled from IFrameSampler (accepts any
// IAsyncEnumerable<FrameSample>) so it is fully testable against a
// synthetic sequence of hand-built frames with no ffmpeg involved.
internal static class ClipFrameMetricsPipeline
{
    public static async IAsyncEnumerable<ClipFrameMetrics> ComputeAsync(
        IAsyncEnumerable<FrameSample> frames,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AnalyzedFrame? previous = null;

        // One BGR working Mat reused for every frame in this run - see
        // Detection.Signals.SignalPipeline's own remarks (this method is
        // its Extraction analogue) and
        // AnalyzedFrame.Create(FrameSample, Mat) for why reuse is safe.
        using var scratchBgr = new Mat();
        try
        {
            // ConfigureAwait(false) required, not optional - see
            // Detection.Signals.SignalPipeline.ComputeAsync's own remarks
            // (this method is its Extraction analogue) and
            // docs/UI_RESPONSIVENESS_AUDIT.md: without it, a UI-thread
            // caller has every per-frame OpenCvSharp continuation below
            // marshaled back onto the UI dispatcher instead of a
            // thread-pool thread, freezing the UI for the run's duration.
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                AnalyzedFrame current;
                try
                {
                    current = AnalyzedFrame.Create(frame, scratchBgr);
                }
                finally
                {
                    frame.Dispose();
                }

                var metrics = ClipFrameMetricsExtractor.Build(previous, current);
                previous?.Dispose();
                previous = current;
                yield return metrics;
            }
        }
        finally
        {
            previous?.Dispose();
        }
    }
}
