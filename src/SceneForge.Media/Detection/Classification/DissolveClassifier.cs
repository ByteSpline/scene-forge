using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Detection.Classification;

// A dissolve/crossfade is a sustained, bell-shaped elevation of
// StructuralDifference across several frames (ramping up then down),
// unlike HardCutClassifier's isolated single-frame spike, and without
// passing through near-black/near-white (that is FadeBlack/Flash's
// signature instead).
internal sealed class DissolveClassifier : ITransitionClassifier
{
    private const double FloorRatio = 0.3;

    public TransitionType Type => TransitionType.Dissolve;

    public IReadOnlyList<TransitionCandidate> Classify(IReadOnlyList<FrameSignalSample> window, TransitionDetectionProfile profile)
    {
        var thresholds = profile.Dissolve;
        var results = new List<TransitionCandidate>();
        var claimed = new bool[window.Count];

        for (var i = 0; i < window.Count; i++)
        {
            if (claimed[i] || window[i].StructuralDifference < thresholds.MinPeakStructuralDifference)
            {
                continue;
            }

            var floor = window[i].StructuralDifference * FloorRatio;

            var left = i;
            while (left > 0 && window[left - 1].StructuralDifference >= floor)
            {
                left--;
            }

            var right = i;
            while (right < window.Count - 1 && window[right + 1].StructuralDifference >= floor)
            {
                right++;
            }

            var peakIndex = left;
            for (var k = left; k <= right; k++)
            {
                if (window[k].StructuralDifference > window[peakIndex].StructuralDifference)
                {
                    peakIndex = k;
                }
            }

            for (var k = left; k <= right; k++)
            {
                claimed[k] = true;
            }

            if (right - left + 1 < thresholds.MinSustainedSamples)
            {
                continue;
            }

            var peakValue = window[peakIndex].StructuralDifference;
            if (window[left].StructuralDifference > thresholds.MaxEdgeRampRatio * peakValue ||
                window[right].StructuralDifference > thresholds.MaxEdgeRampRatio * peakValue)
            {
                continue;
            }

            var maxBlackOrWhite = 0.0;
            for (var k = left; k <= right; k++)
            {
                maxBlackOrWhite = Math.Max(maxBlackOrWhite, Math.Max(window[k].BlackScore, window[k].WhiteScore));
            }

            if (maxBlackOrWhite > thresholds.MaxPeakBlackOrWhiteScore)
            {
                continue;
            }

            results.Add(new TransitionCandidate
            {
                Type = TransitionType.Dissolve,
                Start = window[left].PreviousTimestamp,
                Peak = window[peakIndex].Timestamp,
                End = window[right].Timestamp,
                Confidence = Math.Clamp(peakValue / Math.Max(peakValue, thresholds.MinPeakStructuralDifference * 2), 0.0, 1.0),
                ContributingSignals = new Dictionary<string, double>
                {
                    [nameof(FrameSignalSample.StructuralDifference)] = peakValue,
                    [nameof(FrameSignalSample.HsvHistogramDistance)] = window[peakIndex].HsvHistogramDistance,
                },
                DiagnosticReason =
                    $"Bell-shaped StructuralDifference elevation over {right - left + 1} frames, peaking at {peakValue:F2} " +
                    $"at {window[peakIndex].Timestamp}, without a black/white pass-through (max {maxBlackOrWhite:F2}).",
            });
        }

        return results;
    }
}
