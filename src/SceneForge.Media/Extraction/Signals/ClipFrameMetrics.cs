namespace SceneForge.Media.Extraction.Signals;

// One sampled frame's worth of measurements for clip scoring/perceptual
// fingerprinting - the shared vocabulary ClipScorer, MotionClassifier, and
// the perceptual-descriptor projection all read from. Deliberately plain
// scalars/small arrays (no OpenCvSharp Mat anywhere in this type), the same
// "everything downstream of the Mat-touching extractor sees only this" rule
// Detection.Signals.FrameSignalSample established for Phase 6 - this is
// what makes ClipScorer/MotionClassifier fully testable with hand-built
// data and no native image dependency at all.
internal sealed record ClipFrameMetrics
{
    public required TimeSpan Timestamp { get; init; }

    // Laplacian-variance focus measure of this frame alone - higher is
    // sharper. Unbounded (same as AnalyzedFrame.LaplacianVariance).
    public required double Sharpness { get; init; }

    // Mean grayscale intensity, 0..1.
    public required double MeanLuminance { get; init; }

    // Fraction of near-black pixels, 0..1.
    public required double BlackScore { get; init; }

    // Fraction of near-white pixels, 0..1.
    public required double WhiteScore { get; init; }

    // Mean edge density across the outer ring of EdgeHistogram's grid
    // (caption/logo/watermark-prone regions), 0..1.
    public required double BorderEdgeDensity { get; init; }

    // Mean edge density across the inner cells of EdgeHistogram's grid, 0..1.
    public required double InteriorEdgeDensity { get; init; }

    // Normalized mean absolute grayscale pixel difference from the
    // previous sampled frame, 0..1. Zero for the very first frame of a
    // stream (there is no previous frame to compare against).
    public required double StructuralDifferenceFromPrevious { get; init; }

    public required ulong PerceptualHash { get; init; }

    // Normalized (sums to ~1) 1D hue histogram.
    public required IReadOnlyList<float> ColorHistogram { get; init; }

    // Edge-density-per-cell grid (each cell independently 0..1 - not
    // normalized to sum to 1 across cells).
    public required IReadOnlyList<float> EdgeHistogram { get; init; }
}
