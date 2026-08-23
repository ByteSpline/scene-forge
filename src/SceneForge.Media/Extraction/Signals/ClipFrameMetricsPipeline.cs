using System.Runtime.CompilerServices;
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
        try
        {
            await foreach (var frame in frames.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                AnalyzedFrame current;
                try
                {
                    current = AnalyzedFrame.Create(frame);
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
