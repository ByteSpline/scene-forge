using SceneForge.Media.Extraction.Signals;

namespace SceneForge.Media.Tests.TestSupport;

// Hand-builds ClipFrameMetrics with known values, for deterministic
// ClipScorer/MotionClassifier/CleanClipScoringSweep unit tests with no
// OpenCvSharp/Mat involvement at all - same role FrameSignalSampleBuilder
// plays for Detection's classifiers.
internal static class ClipFrameMetricsBuilder
{
    public static ClipFrameMetrics Sample(
        double timestampSeconds,
        double sharpness = 100,
        double meanLuminance = 0.5,
        double blackScore = 0,
        double whiteScore = 0,
        double borderEdgeDensity = 0.05,
        double interiorEdgeDensity = 0.05,
        double structuralDifferenceFromPrevious = 0.02,
        ulong perceptualHash = 0,
        IReadOnlyList<float>? colorHistogram = null,
        IReadOnlyList<float>? edgeHistogram = null) => new()
        {
            Timestamp = TimeSpan.FromSeconds(timestampSeconds),
            Sharpness = sharpness,
            MeanLuminance = meanLuminance,
            BlackScore = blackScore,
            WhiteScore = whiteScore,
            BorderEdgeDensity = borderEdgeDensity,
            InteriorEdgeDensity = interiorEdgeDensity,
            StructuralDifferenceFromPrevious = structuralDifferenceFromPrevious,
            PerceptualHash = perceptualHash,
            ColorHistogram = colorHistogram ?? [],
            EdgeHistogram = edgeHistogram ?? [],
        };
}
