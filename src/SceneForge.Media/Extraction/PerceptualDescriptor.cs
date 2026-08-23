namespace SceneForge.Media.Extraction;

// Coarse, heuristic bucketing of a clip's frame-to-frame motion, derived
// from the same mean structural-difference value ClipScore.Stability is
// built from (see MotionClassifier) - never claimed as a precise motion
// measurement, only a rough visual-similarity signal for clustering.
public enum MotionClass
{
    Static,
    Subtle,
    Moderate,
    High,
}

// A lightweight, no-heavy-ML-model fingerprint of one representative frame
// (plus one whole-clip motion bucket) used only to find visually similar
// CleanClip candidates - see VisualClusterer. Deliberately cheap: a 64-bit
// hash and two small float arrays, not the frame's pixels themselves, so
// carrying one of these per candidate never approaches "buffering full
// video" territory (CLAUDE.md rule 7).
public sealed record PerceptualDescriptor
{
    // 64-bit perceptual hash (mean-thresholded low-frequency 8x8 DCT block
    // of a 32x32 grayscale resize) of the clip's representative frame - see
    // PerceptualHashExtractor. Near-duplicate frames produce hashes with a
    // small Hamming distance; unrelated frames do not.
    public required ulong PerceptualHash { get; init; }

    // Normalized (sums to ~1) 1D hue histogram of the representative frame.
    public required IReadOnlyList<float> ColorHistogram { get; init; }

    // Edge-density-per-cell grid of the representative frame (each cell
    // independently 0..1 - not normalized to sum to 1 across cells, see
    // EdgeHistogramExtractor) - also the basis for
    // ClipScore.OverlaySuspicion's border-vs-interior split.
    public required IReadOnlyList<float> EdgeHistogram { get; init; }

    public required MotionClass Motion { get; init; }
}
