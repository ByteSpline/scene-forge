using SceneForge.Media.Extraction.Signals;

namespace SceneForge.Media.Extraction.Scoring;

// Buckets a clip's mean frame-to-frame structural difference into one of
// four coarse motion classes, reusing the same StabilityReferenceValue
// ClipScorer normalizes Stability against - so "High" motion and a
// near-zero Stability score are always describing the same underlying
// measurement, never two independently-tuned notions of "a lot of motion".
// Heuristic bucketing only (CLAUDE.md rule 10) - not a calibrated motion
// estimator.
internal static class MotionClassifier
{
    public static MotionClass Classify(double meanStructuralDifference, CleanClipScoringOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ratio = meanStructuralDifference / options.StabilityReferenceValue;

        return ratio switch
        {
            < 0.2 => MotionClass.Static,
            < 0.6 => MotionClass.Subtle,
            < 1.0 => MotionClass.Moderate,
            _ => MotionClass.High,
        };
    }

    public static double MeanStructuralDifference(IReadOnlyList<ClipFrameMetrics> framesInWindow) =>
        framesInWindow.Count == 0 ? 0.0 : framesInWindow.Average(f => f.StructuralDifferenceFromPrevious);
}
