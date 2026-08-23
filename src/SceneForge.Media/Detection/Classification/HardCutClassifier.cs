using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Detection.Classification;

// A hard cut is an isolated single-pair spike in StructuralDifference
// and/or HsvHistogramDistance - what tells it apart from Dissolve is not
// the magnitude of the spike but that it does NOT sustain across
// neighboring pairs (a dissolve's difference ramps up and down over
// several frames). The two signals gate independently (either qualifies,
// each judged isolated on its own terms) rather than requiring both at
// once: HsvHistogramDistance is computed over Hue/Saturation only (see
// AnalyzedFrame) and is near-zero for a same-hue brightness-only cut
// between desaturated/grayscale content, while StructuralDifference alone
// can be small for a same-luma hue-only cut - requiring both would miss
// either case.
internal sealed class HardCutClassifier : ITransitionClassifier
{
    public TransitionType Type => TransitionType.HardCut;

    public IReadOnlyList<TransitionCandidate> Classify(IReadOnlyList<FrameSignalSample> window, TransitionDetectionProfile profile)
    {
        var thresholds = profile.HardCut;
        var results = new List<TransitionCandidate>();

        for (var i = 0; i < window.Count; i++)
        {
            var sample = window[i];

            var structuralQualifies = sample.StructuralDifference >= thresholds.MinStructuralDifference
                && IsIsolatedSpike(window, i, s => s.StructuralDifference);
            var hsvQualifies = sample.HsvHistogramDistance >= thresholds.MinHsvHistogramDistance
                && IsIsolatedSpike(window, i, s => s.HsvHistogramDistance);

            if (!structuralQualifies && !hsvQualifies)
            {
                continue;
            }

            var structuralExcess = Excess(sample.StructuralDifference, thresholds.MinStructuralDifference);
            var hsvExcess = Excess(sample.HsvHistogramDistance, thresholds.MinHsvHistogramDistance);

            results.Add(new TransitionCandidate
            {
                Type = TransitionType.HardCut,
                Start = sample.PreviousTimestamp,
                Peak = sample.Timestamp,
                End = sample.Timestamp,
                Confidence = Math.Clamp(Math.Max(structuralExcess, hsvExcess), 0.0, 1.0),
                ContributingSignals = new Dictionary<string, double>
                {
                    [nameof(FrameSignalSample.StructuralDifference)] = sample.StructuralDifference,
                    [nameof(FrameSignalSample.HsvHistogramDistance)] = sample.HsvHistogramDistance,
                },
                DiagnosticReason =
                    $"Isolated single-frame spike at {sample.Timestamp}: StructuralDifference={sample.StructuralDifference:F2} " +
                    $"(threshold {thresholds.MinStructuralDifference:F2}, qualifies={structuralQualifies}), " +
                    $"HsvHistogramDistance={sample.HsvHistogramDistance:F2} (threshold {thresholds.MinHsvHistogramDistance:F2}, " +
                    $"qualifies={hsvQualifies}), not sustained in neighboring frames.",
            });
        }

        return results;
    }

    // A spike is isolated when its neighbors are markedly lower - a
    // sustained ramp (dissolve) would have comparable neighbor values.
    private static bool IsIsolatedSpike(IReadOnlyList<FrameSignalSample> window, int index, Func<FrameSignalSample, double> selector)
    {
        const double NeighborRatio = 0.3;

        var value = selector(window[index]);
        if (index > 0 && selector(window[index - 1]) >= value * NeighborRatio)
        {
            return false;
        }

        if (index < window.Count - 1 && selector(window[index + 1]) >= value * NeighborRatio)
        {
            return false;
        }

        return true;
    }

    private static double Excess(double value, double threshold) =>
        threshold >= 1.0 ? (value >= threshold ? 1.0 : 0.0) : (value - threshold) / (1.0 - threshold);
}
