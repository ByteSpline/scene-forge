using SceneForge.Media.Extraction.Signals;

namespace SceneForge.Media.Extraction.Clustering;

// Combined perceptual distance between two descriptors: weighted sum of
// normalized pHash Hamming distance, color-histogram distance, and
// edge-histogram distance, plus a flat penalty when their MotionClass
// differs. Lower means more visually similar; see VisualClusterer for how
// this feeds clustering. Pure, no OpenCvSharp.
internal static class PerceptualDistance
{
    public static double Compute(PerceptualDescriptor a, PerceptualDescriptor b, ClusteringOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hashDistance = PerceptualHashExtractor.HammingDistance(a.PerceptualHash, b.PerceptualHash) / 64.0;
        var colorDistance = MeanAbsoluteDifference(a.ColorHistogram, b.ColorHistogram);
        var edgeDistance = MeanAbsoluteDifference(a.EdgeHistogram, b.EdgeHistogram);
        var motionPenalty = a.Motion == b.Motion ? 0.0 : options.MotionMismatchPenalty;

        return (options.HashWeight * hashDistance)
            + (options.ColorHistogramWeight * colorDistance)
            + (options.EdgeHistogramWeight * edgeDistance)
            + motionPenalty;
    }

    // Two empty/mismatched-length arrays (e.g. one descriptor had no
    // representative frame at all) are treated as maximally dissimilar
    // rather than throwing - a defensive floor, not an expected path.
    private static double MeanAbsoluteDifference(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count == 0 || b.Count == 0 || a.Count != b.Count)
        {
            return 1.0;
        }

        double sum = 0;
        for (var i = 0; i < a.Count; i++)
        {
            sum += Math.Abs(a[i] - b[i]);
        }

        return Math.Clamp(sum / a.Count, 0.0, 1.0);
    }
}
