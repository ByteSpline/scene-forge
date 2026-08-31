using System.Runtime.CompilerServices;
using OpenCvSharp;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Detection.Signals;

// Streams FrameSignalSample per consecutive FrameSample pair. This is the
// only place in Detection that touches AnalyzedFrame/OpenCvSharp Mats
// directly outside AnalyzedFrame itself - classifiers and fusion only ever
// see FrameSignalSample's plain scalars. At most two AnalyzedFrame instances
// (previous, current) are ever alive at once, each disposed as soon as it is
// no longer needed - memory stays flat regardless of video length
// (CLAUDE.md rule 6/7). Decoupled from IFrameSampler (accepts any
// IAsyncEnumerable<FrameSample>) so it is fully testable against a synthetic
// sequence of hand-built frames with no ffmpeg involved.
internal sealed class SignalPipeline
{
    private readonly HsvHistogramDistanceExtractor _hsvHistogramDistance = new();
    private readonly LuminanceDeltaExtractor _luminanceDelta = new();
    private readonly EdgeDensityDeltaExtractor _edgeDensityDelta = new();
    private readonly LaplacianBlurChangeExtractor _laplacianBlurChange = new();
    private readonly StructuralDifferenceExtractor _structuralDifference = new();
    private readonly BlackScoreExtractor _blackScore = new();
    private readonly WhiteScoreExtractor _whiteScore = new();
    private readonly GlobalMotionEstimateExtractor _globalMotion = new();

    public async IAsyncEnumerable<FrameSignalSample> ComputeAsync(
        IAsyncEnumerable<FrameSample> frames,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AnalyzedFrame? previous = null;

        // One BGR working Mat reused for every frame in this run (all
        // frames share the same fixed dimensions - see FrameDimensions),
        // instead of AnalyzedFrame.Create allocating a fresh native buffer
        // every single frame. See AnalyzedFrame.Create(FrameSample, Mat)'s
        // own remarks for why reuse is safe (Gray/HsvHistogram/Edges are
        // always independently-allocated, never views over this buffer).
        using var scratchBgr = new Mat();
        try
        {
            // ConfigureAwait(false) is required here, not optional -
            // WithCancellation and ConfigureAwait are independent knobs, and
            // omitting this one is a real, previously-shipped UI-freeze bug
            // (see docs/UI_RESPONSIVENESS_AUDIT.md): a caller that invokes
            // this whole pipeline directly from a UI thread (as
            // AnalysisProgressViewModel does) causes every per-frame
            // continuation below - the actual OpenCvSharp signal-extraction
            // work, the dominant CPU cost in this pipeline - to be marshaled
            // back onto that UI thread's dispatcher queue instead of running
            // on a thread-pool thread, effectively serializing the whole
            // video's analysis onto the UI message loop and freezing it,
            // Cancel button included, for the run's full duration.
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

                if (previous is null)
                {
                    previous = current;
                    continue;
                }

                var sample = Build(previous, current);
                previous.Dispose();
                previous = current;
                yield return sample;
            }
        }
        finally
        {
            previous?.Dispose();
        }
    }

    private FrameSignalSample Build(AnalyzedFrame previous, AnalyzedFrame current) => new()
    {
        PreviousTimestamp = previous.Timestamp,
        Timestamp = current.Timestamp,
        HsvHistogramDistance = _hsvHistogramDistance.Extract(previous, current),
        LuminanceDelta = _luminanceDelta.Extract(previous, current),
        EdgeDensityDelta = _edgeDensityDelta.Extract(previous, current),
        LaplacianBlurChange = _laplacianBlurChange.Extract(previous, current),
        BlackScore = _blackScore.Extract(current),
        WhiteScore = _whiteScore.Extract(current),
        CurrentLaplacianVariance = current.LaplacianVariance,
        CurrentEdgeDensity = current.EdgeDensity,
        StructuralDifference = _structuralDifference.Extract(previous, current),
        GlobalMotion = _globalMotion.Extract(previous, current),
    };
}
