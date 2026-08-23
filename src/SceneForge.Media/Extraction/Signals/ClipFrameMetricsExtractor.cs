using OpenCvSharp;
using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Extraction.Signals;

// Builds one ClipFrameMetrics from an already-decoded AnalyzedFrame (reused
// directly from Detection.Signals - both live in SceneForge.Media, and
// AnalyzedFrame's gray/luminance/sharpness/black-white scalars are exactly
// what clip scoring needs too, so recomputing them would be pure
// duplication). previous is null only for the very first frame of a
// stream, in which case StructuralDifferenceFromPrevious is 0 (there is
// nothing to compare against yet). This is the only place in Extraction
// that touches AnalyzedFrame/OpenCvSharp Mats directly - everything
// downstream (ClipScorer, MotionClassifier, VisualClusterer) only ever sees
// ClipFrameMetrics's plain scalars/arrays.
internal static class ClipFrameMetricsExtractor
{
    public static ClipFrameMetrics Build(AnalyzedFrame? previous, AnalyzedFrame current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var edgeHistogram = EdgeHistogramExtractor.Extract(current.Gray);
        var colorHistogram = ColorHistogramExtractor.Extract(current);
        var perceptualHash = PerceptualHashExtractor.Extract(current.Gray);
        var structuralDifference = previous is null ? 0.0 : StructuralDifference(previous, current);

        return new ClipFrameMetrics
        {
            Timestamp = current.Timestamp,
            Sharpness = current.LaplacianVariance,
            MeanLuminance = current.MeanLuminance,
            BlackScore = current.BlackScore,
            WhiteScore = current.WhiteScore,
            BorderEdgeDensity = EdgeHistogramExtractor.BorderDensity(edgeHistogram),
            InteriorEdgeDensity = EdgeHistogramExtractor.InteriorDensity(edgeHistogram),
            StructuralDifferenceFromPrevious = structuralDifference,
            PerceptualHash = perceptualHash,
            ColorHistogram = colorHistogram,
            EdgeHistogram = edgeHistogram,
        };
    }

    private static double StructuralDifference(AnalyzedFrame previous, AnalyzedFrame current)
    {
        using var diff = new Mat();
        Cv2.Absdiff(previous.Gray, current.Gray, diff);
        return Cv2.Mean(diff).Val0 / 255.0;
    }
}
