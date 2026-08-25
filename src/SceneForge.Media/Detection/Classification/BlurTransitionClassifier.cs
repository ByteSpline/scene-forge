using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Detection.Classification;

// Finds the window's blurriest point (lowest CurrentLaplacianVariance)
// relative to the window's own sharpest point (baseline), requiring both a
// fractional variance drop and a corroborating edge-density drop - a blur
// transition loses fine edges, which a dissolve between two already-sharp
// scenes would not.
internal sealed class BlurTransitionClassifier : ITransitionClassifier
{
    public TransitionType Type => TransitionType.BlurTransition;

    public IReadOnlyList<TransitionCandidate> Classify(IReadOnlyList<FrameSignalSample> window, TransitionDetectionProfile profile)
    {
        var thresholds = profile.BlurTransition;

        if (window.Count == 0)
        {
            return [];
        }

        var baseline = window[0].CurrentLaplacianVariance;
        var minIndex = 0;
        for (var i = 1; i < window.Count; i++)
        {
            baseline = Math.Max(baseline, window[i].CurrentLaplacianVariance);
            if (window[i].CurrentLaplacianVariance < window[minIndex].CurrentLaplacianVariance)
            {
                minIndex = i;
            }
        }

        if (baseline <= 1e-9)
        {
            return [];
        }

        var dropRatio = (baseline - window[minIndex].CurrentLaplacianVariance) / baseline;
        if (dropRatio < thresholds.MinLaplacianDropRatio)
        {
            return [];
        }

        var recoveryLevel = baseline * (1 - (thresholds.MinLaplacianDropRatio * 0.5));

        var left = minIndex;
        while (left > 0 && window[left - 1].CurrentLaplacianVariance < recoveryLevel)
        {
            left--;
        }

        var right = minIndex;
        while (right < window.Count - 1 && window[right + 1].CurrentLaplacianVariance < recoveryLevel)
        {
            right++;
        }

        if (right - left + 1 < thresholds.MinSustainedSamples)
        {
            return [];
        }

        var baselineEdge = window[0].CurrentEdgeDensity;
        for (var i = 1; i < window.Count; i++)
        {
            baselineEdge = Math.Max(baselineEdge, window[i].CurrentEdgeDensity);
        }

        var edgeDrop = baselineEdge - window[minIndex].CurrentEdgeDensity;
        if (edgeDrop < thresholds.MinEdgeDensityDrop)
        {
            return [];
        }

        return
        [
            new TransitionCandidate
            {
                Type = TransitionType.BlurTransition,
                Start = window[left].PreviousTimestamp,
                Peak = window[minIndex].Timestamp,
                End = window[right].Timestamp,
                Confidence = Math.Clamp(dropRatio, 0.0, 1.0),
                ContributingSignals = new Dictionary<string, double>
                {
                    ["LaplacianDropRatio"] = dropRatio,
                    [nameof(FrameSignalSample.CurrentEdgeDensity) + "Drop"] = edgeDrop,
                },
                DiagnosticReason =
                    $"Laplacian variance dropped {dropRatio:P0} from the window's baseline sharpness at {window[minIndex].Timestamp}, " +
                    $"with a corresponding edge-density drop of {edgeDrop:F2}, recovering by {window[right].Timestamp}.",
            },
        ];
    }
}
