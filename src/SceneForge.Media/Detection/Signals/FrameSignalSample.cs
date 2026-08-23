namespace SceneForge.Media.Detection.Signals;

// One consecutive frame pair's worth of signal measurements - the shared
// vocabulary every classifier reads from. Six of the seven required signals
// are deltas between PreviousTimestamp and Timestamp; BlackScore/WhiteScore
// are the current frame's own absolute score (see BlackWhiteFrameScoreExtractor).
public sealed record FrameSignalSample
{
    public required TimeSpan PreviousTimestamp { get; init; }

    public required TimeSpan Timestamp { get; init; }

    // Bhattacharyya distance (0..1, 1 = maximally different) between the
    // two frames' HSV histograms.
    public required double HsvHistogramDistance { get; init; }

    // Current mean luminance minus previous mean luminance, signed, -1..1.
    // Negative means the frame got darker.
    public required double LuminanceDelta { get; init; }

    // Current edge density minus previous edge density, signed, -1..1.
    public required double EdgeDensityDelta { get; init; }

    // Current Laplacian variance minus previous, signed. Not bounded to
    // -1..1 (Laplacian variance is an unbounded focus measure); classifiers
    // reason about it as a ratio relative to a window baseline.
    public required double LaplacianBlurChange { get; init; }

    // Current frame's own black-pixel fraction, 0..1 (not a delta).
    public required double BlackScore { get; init; }

    // Current frame's own white-pixel fraction, 0..1 (not a delta).
    public required double WhiteScore { get; init; }

    // Current frame's own absolute Laplacian variance and edge density -
    // auxiliary to the required LaplacianBlurChange/EdgeDensityDelta
    // signals, carried alongside them so BlurTransitionClassifier can
    // reason about a fractional drop from a window's own baseline sharpness
    // rather than only ever seeing frame-to-frame deltas with no absolute
    // reference point.
    public required double CurrentLaplacianVariance { get; init; }

    public required double CurrentEdgeDensity { get; init; }

    // Normalized mean absolute grayscale pixel difference, 0..1.
    public required double StructuralDifference { get; init; }

    public required GlobalMotionEstimate GlobalMotion { get; init; }
}

// Global motion estimate reduced from a dense optical flow field to a
// handful of scalars immediately after computation - the dense flow Mat
// itself is never retained (see GlobalMotionEstimateExtractor).
public sealed record GlobalMotionEstimate
{
    // Mean horizontal/vertical flow, in pixels, across the analysis frame.
    public required double MeanDx { get; init; }

    public required double MeanDy { get; init; }

    // Overall motion magnitude, normalized by frame diagonal, 0..~1 for
    // typical motion (can exceed 1 for extreme motion).
    public required double Magnitude { get; init; }

    // -1..1. Near +1: flow vectors point outward from frame center
    // (expanding/zoom-out-like content). Near -1: vectors point inward
    // (contracting/zoom-in-like content). Near 0: no consistent radial
    // pattern.
    public required double RadialOutwardScore { get; init; }

    // 0..1. How consistently all flow-grid cells agree on one direction -
    // 1 means every cell's vector points the same way (a wipe/slide
    // signature); low values mean flow is scattered or radial rather than
    // uniform.
    public required double DirectionalConsistency { get; init; }
}
