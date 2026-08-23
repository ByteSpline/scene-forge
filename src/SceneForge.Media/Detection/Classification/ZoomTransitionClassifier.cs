using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Detection.Classification;

// Finds maximal runs where GlobalMotion has a strong, consistently-signed
// radial pattern - flow vectors pointing outward from (or inward toward)
// the frame center, the zoom signature, as opposed to DirectionalSwipe's
// uniform (non-radial) motion. Splits a run if the radial sign flips
// (zoom-in immediately followed by zoom-out is two events, not one).
internal sealed class ZoomTransitionClassifier : ITransitionClassifier
{
    public TransitionType Type => TransitionType.ZoomTransition;

    public IReadOnlyList<TransitionCandidate> Classify(IReadOnlyList<FrameSignalSample> window, TransitionDetectionProfile profile)
    {
        var thresholds = profile.ZoomTransition;
        var results = new List<TransitionCandidate>();

        bool Qualifies(int i) =>
            window[i].GlobalMotion.Magnitude >= thresholds.MinMotionMagnitude &&
            Math.Abs(window[i].GlobalMotion.RadialOutwardScore) >= thresholds.MinRadialOutwardScoreMagnitude;

        foreach (var (start, end) in ContiguousRunFinder.FindRuns(window.Count, Qualifies, thresholds.MinSustainedSamples))
        {
            foreach (var (subStart, subEnd) in SplitBySign(window, start, end))
            {
                results.Add(BuildCandidate(window, subStart, subEnd));
            }
        }

        return results;
    }

    // Splits [start, end] at any point where RadialOutwardScore changes
    // sign, so a zoom-in run is never merged with an immediately following
    // zoom-out run into one misleading candidate.
    private static IEnumerable<(int Start, int End)> SplitBySign(IReadOnlyList<FrameSignalSample> window, int start, int end)
    {
        var segmentStart = start;
        var segmentSign = Math.Sign(window[start].GlobalMotion.RadialOutwardScore);

        for (var i = start + 1; i <= end; i++)
        {
            var sign = Math.Sign(window[i].GlobalMotion.RadialOutwardScore);
            if (sign != segmentSign && sign != 0)
            {
                yield return (segmentStart, i - 1);
                segmentStart = i;
                segmentSign = sign;
            }
        }

        yield return (segmentStart, end);
    }

    private static TransitionCandidate BuildCandidate(IReadOnlyList<FrameSignalSample> window, int start, int end)
    {
        var peakIndex = start;
        for (var i = start; i <= end; i++)
        {
            if (Math.Abs(window[i].GlobalMotion.RadialOutwardScore) > Math.Abs(window[peakIndex].GlobalMotion.RadialOutwardScore))
            {
                peakIndex = i;
            }
        }

        var peak = window[peakIndex];
        var direction = peak.GlobalMotion.RadialOutwardScore >= 0 ? "outward (zoom-out-like)" : "inward (zoom-in-like)";

        return new TransitionCandidate
        {
            Type = TransitionType.ZoomTransition,
            Start = window[start].PreviousTimestamp,
            Peak = peak.Timestamp,
            End = window[end].Timestamp,
            Confidence = Math.Clamp(Math.Abs(peak.GlobalMotion.RadialOutwardScore), 0.0, 1.0),
            ContributingSignals = new Dictionary<string, double>
            {
                [nameof(FrameSignalSample.GlobalMotion) + "." + nameof(GlobalMotionEstimate.RadialOutwardScore)] = peak.GlobalMotion.RadialOutwardScore,
                [nameof(FrameSignalSample.GlobalMotion) + "." + nameof(GlobalMotionEstimate.Magnitude)] = peak.GlobalMotion.Magnitude,
            },
            DiagnosticReason =
                $"Sustained {direction} radial flow from {window[start].PreviousTimestamp} to {window[end].Timestamp}, " +
                $"peaking at RadialOutwardScore={peak.GlobalMotion.RadialOutwardScore:F2} (magnitude {peak.GlobalMotion.Magnitude:F2}).",
        };
    }
}
