using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Detection.Classification;

// Finds maximal runs where GlobalMotion has a strong, consistently uniform
// direction across the whole frame - the wipe/slide signature, as opposed
// to ZoomTransition's radial pattern.
internal sealed class DirectionalSwipeClassifier : ITransitionClassifier
{
    public TransitionType Type => TransitionType.DirectionalSwipe;

    public IReadOnlyList<TransitionCandidate> Classify(IReadOnlyList<FrameSignalSample> window, TransitionDetectionProfile profile)
    {
        var thresholds = profile.DirectionalSwipe;
        List<TransitionCandidate>? results = null;

        bool Qualifies(int i) =>
            window[i].GlobalMotion.Magnitude >= thresholds.MinMotionMagnitude &&
            window[i].GlobalMotion.DirectionalConsistency >= thresholds.MinDirectionalConsistency;

        foreach (var (start, end) in ContiguousRunFinder.FindRuns(window.Count, Qualifies, thresholds.MinSustainedSamples))
        {
            (results ??= []).Add(BuildCandidate(window, start, end));
        }

        return results ?? [];
    }

    private static TransitionCandidate BuildCandidate(IReadOnlyList<FrameSignalSample> window, int start, int end)
    {
        var peakIndex = start;
        for (var i = start; i <= end; i++)
        {
            if (window[i].GlobalMotion.Magnitude > window[peakIndex].GlobalMotion.Magnitude)
            {
                peakIndex = i;
            }
        }

        var peak = window[peakIndex];

        return new TransitionCandidate
        {
            Type = TransitionType.DirectionalSwipe,
            Start = window[start].PreviousTimestamp,
            Peak = peak.Timestamp,
            End = window[end].Timestamp,
            Confidence = Math.Clamp(peak.GlobalMotion.DirectionalConsistency, 0.0, 1.0),
            ContributingSignals = new Dictionary<string, double>
            {
                [nameof(FrameSignalSample.GlobalMotion) + "." + nameof(GlobalMotionEstimate.DirectionalConsistency)] = peak.GlobalMotion.DirectionalConsistency,
                [nameof(FrameSignalSample.GlobalMotion) + "." + nameof(GlobalMotionEstimate.Magnitude)] = peak.GlobalMotion.Magnitude,
            },
            DiagnosticReason =
                $"Sustained uniform-direction flow from {window[start].PreviousTimestamp} to {window[end].Timestamp}, " +
                $"peaking at DirectionalConsistency={peak.GlobalMotion.DirectionalConsistency:F2} (magnitude {peak.GlobalMotion.Magnitude:F2}).",
        };
    }
}
