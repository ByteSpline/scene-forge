namespace SceneForge.Media.Detection.Fusion;

// All numeric knobs that drive signal fusion, classifier thresholds, and
// candidate merging/buffering, grouped by classifier. Every classifier
// reads its thresholds from here rather than embedding literals, so the
// whole fusion behavior for a given TransitionDetectionProfileVersion is
// visible in one place and reproducible (CLAUDE.md rule 10: measured and
// versioned, never a hidden magic threshold). Callers normally obtain one
// via TransitionDetectionProfiles.GetDefaults(version); every value remains
// overridable via `with { ... }` for advanced callers, and every setter
// clamps to a safe range so an override can never turn this into unbounded
// or nonsensical work.
public sealed record TransitionDetectionProfile
{
    private static readonly TimeSpan MinMaxTransitionDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxMaxTransitionDuration = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan MinBufferDuration = TimeSpan.Zero;
    private static readonly TimeSpan MaxBufferDuration = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _maxTransitionDuration = TimeSpan.FromSeconds(2.5);
    private readonly TimeSpan _mergeGapTolerance = TimeSpan.FromMilliseconds(150);
    private readonly TimeSpan _preBufferDuration = TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan _postBufferDuration = TimeSpan.FromMilliseconds(100);

    public required TransitionDetectionProfileVersion Version { get; init; }

    // Upper bound on how long any single transition may span. Bounds the
    // classifier sliding window's memory footprint independent of video
    // length (CLAUDE.md rule 6/7): no classifier ever looks further back
    // than this.
    public TimeSpan MaxTransitionDuration
    {
        get => _maxTransitionDuration;
        init => _maxTransitionDuration = Clamp(value, MinMaxTransitionDuration, MaxMaxTransitionDuration);
    }

    // Two candidates whose intervals are within this gap of each other (or
    // overlapping) are merged into one detection by TransitionFuser.
    public TimeSpan MergeGapTolerance
    {
        get => _mergeGapTolerance;
        init => _mergeGapTolerance = Clamp(value, TimeSpan.Zero, MaxMaxTransitionDuration);
    }

    // Safety margin subtracted from every detection's Start before export,
    // clamped to zero at the timeline origin.
    public TimeSpan PreBufferDuration
    {
        get => _preBufferDuration;
        init => _preBufferDuration = Clamp(value, MinBufferDuration, MaxBufferDuration);
    }

    // Safety margin added to every detection's End before export, clamped
    // to the source duration.
    public TimeSpan PostBufferDuration
    {
        get => _postBufferDuration;
        init => _postBufferDuration = Clamp(value, MinBufferDuration, MaxBufferDuration);
    }

    public required HardCutThresholds HardCut { get; init; }

    public required FadeBlackThresholds FadeBlack { get; init; }

    public required DissolveThresholds Dissolve { get; init; }

    public required FlashThresholds Flash { get; init; }

    public required BlurTransitionThresholds BlurTransition { get; init; }

    public required ZoomTransitionThresholds ZoomTransition { get; init; }

    public required DirectionalSwipeThresholds DirectionalSwipe { get; init; }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;
}

public sealed record HardCutThresholds
{
    private readonly double _minStructuralDifference = 0.35;
    private readonly double _minHsvHistogramDistance = 0.35;

    // Minimum single-pair structural difference (0..1) to consider a cut.
    public double MinStructuralDifference
    {
        get => _minStructuralDifference;
        init => _minStructuralDifference = Ratio.Clamp(value);
    }

    // Minimum single-pair HSV histogram distance (0..1) to consider a cut.
    public double MinHsvHistogramDistance
    {
        get => _minHsvHistogramDistance;
        init => _minHsvHistogramDistance = Ratio.Clamp(value);
    }

    // A cut must resolve within this many consecutive pair-samples - a
    // sustained elevated difference belongs to Dissolve, not HardCut.
    public int MaxSpanSamples { get; init; } = 1;
}

public sealed record FadeBlackThresholds
{
    private readonly double _minPeakBlackScore = 0.8;
    private readonly double _minTrendConsistency = 0.7;

    // BlackScore the peak sample must reach for the window to be
    // considered a genuine pass through (near) full black.
    public double MinPeakBlackScore
    {
        get => _minPeakBlackScore;
        init => _minPeakBlackScore = Ratio.Clamp(value);
    }

    // Fraction of pair-steps in the window whose signed LuminanceDelta
    // agrees with the overall trend direction (monotonic-ish ramp, not
    // noise).
    public double MinTrendConsistency
    {
        get => _minTrendConsistency;
        init => _minTrendConsistency = Ratio.Clamp(value);
    }

    // Minimum number of pair-samples the ramp (start..peak or peak..end)
    // must span - rules out a single-pair spike (that is a HardCut/Flash).
    public int MinRampSamples { get; init; } = 2;
}

public sealed record DissolveThresholds
{
    private readonly double _minPeakStructuralDifference = 0.06;
    private readonly double _maxPeakBlackOrWhiteScore = 0.5;
    private readonly double _maxEdgeRampRatio = 0.95;

    // Minimum peak structural difference (0..1) - lower than HardCut's
    // because a dissolve's per-pair difference is spread across many
    // frames rather than concentrated in one.
    public double MinPeakStructuralDifference
    {
        get => _minPeakStructuralDifference;
        init => _minPeakStructuralDifference = Ratio.Clamp(value);
    }

    public int MinSustainedSamples { get; init; } = 3;

    // A window whose peak BlackScore or WhiteScore exceeds this belongs to
    // FadeBlack/Flash instead, not Dissolve.
    public double MaxPeakBlackOrWhiteScore
    {
        get => _maxPeakBlackOrWhiteScore;
        init => _maxPeakBlackOrWhiteScore = Ratio.Clamp(value);
    }

    // The window's first/last sample's structural difference must be no
    // more than this fraction of the peak. Kept deliberately permissive
    // (default near 1.0): measured against real xfade-crossfade content,
    // structural difference stays elevated for the whole blend and then
    // drops sharply the instant blending ends, rather than tapering off
    // symmetrically like a textbook bell curve - this mainly guards
    // against a completely flat plateau that never falls off at all.
    public double MaxEdgeRampRatio
    {
        get => _maxEdgeRampRatio;
        init => _maxEdgeRampRatio = Ratio.Clamp(value);
    }
}

public sealed record FlashThresholds
{
    private readonly double _minPeakWhiteScore = 0.7;
    private static readonly TimeSpan MinAllowedMaxDuration = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MaxAllowedMaxDuration = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _maxDuration = TimeSpan.FromMilliseconds(300);

    // WhiteScore the peak sample must reach.
    public double MinPeakWhiteScore
    {
        get => _minPeakWhiteScore;
        init => _minPeakWhiteScore = Ratio.Clamp(value);
    }

    // A window is only a Flash (not a fade-to/from-white) if its whole
    // elevated-WhiteScore span is shorter than this.
    public TimeSpan MaxDuration
    {
        get => _maxDuration;
        init => _maxDuration = value < MinAllowedMaxDuration
            ? MinAllowedMaxDuration
            : value > MaxAllowedMaxDuration ? MaxAllowedMaxDuration : value;
    }
}

public sealed record BlurTransitionThresholds
{
    private readonly double _minLaplacianDropRatio = 0.5;
    private readonly double _minEdgeDensityDrop = 0.02;

    // Minimum fractional drop of Laplacian variance (focus measure) from
    // the window's baseline to its lowest point.
    public double MinLaplacianDropRatio
    {
        get => _minLaplacianDropRatio;
        init => _minLaplacianDropRatio = Ratio.Clamp(value);
    }

    // Minimum accompanying drop in edge density (0..1 magnitude) - a blur
    // transition loses fine edges, distinguishing it from e.g. a dissolve
    // between two already-sharp scenes.
    public double MinEdgeDensityDrop
    {
        get => _minEdgeDensityDrop;
        init => _minEdgeDensityDrop = Ratio.Clamp(value);
    }

    public int MinSustainedSamples { get; init; } = 2;
}

// Zoom and DirectionalSwipe both gate on GlobalMotion.Magnitude before
// trusting a shape score (radial or directional). Measured against the
// synthetic fixture matrix (docs/PHASE_06_REPORT.md), normalized motion
// magnitude for genuine zoom/swipe content on small (~160x90) analysis
// frames peaks around 0.01-0.03, while quiet non-transition content
// (testsrc2's own gentle internal motion) peaks around 0.003-0.005 - the
// thresholds below sit between those two measured regimes, not at a
// theoretical "ideal" value.
public sealed record ZoomTransitionThresholds
{
    private readonly double _minRadialOutwardScoreMagnitude = 0.15;
    private readonly double _minMotionMagnitude = 0.007;

    // Minimum |RadialOutwardScore| (0..1) - large magnitude in either sign
    // means a consistent expanding-or-contracting flow field, which is the
    // zoom signature.
    public double MinRadialOutwardScoreMagnitude
    {
        get => _minRadialOutwardScoreMagnitude;
        init => _minRadialOutwardScoreMagnitude = Ratio.Clamp(value);
    }

    // Floor on overall motion magnitude, so a near-static frame pair's
    // noisy radial score can't false-positive.
    public double MinMotionMagnitude
    {
        get => _minMotionMagnitude;
        init => _minMotionMagnitude = Math.Max(0, value);
    }

    // Deliberately 1, not 2+: measured radial score is noisy sample-to-
    // sample even within a genuine zoom (see remarks above the type) - a
    // strict multi-sample-run requirement missed real zooms in the fixture
    // matrix. This is a real, measured precision/recall trade-off (more
    // isolated-frame false positives are possible), not an oversight.
    public int MinSustainedSamples { get; init; } = 1;
}

public sealed record DirectionalSwipeThresholds
{
    private readonly double _minDirectionalConsistency = 0.55;
    private readonly double _minMotionMagnitude = 0.007;

    // Minimum DirectionalConsistency (0..1) - close to 1 means motion
    // vectors across the frame all point the same way, the wipe/slide
    // signature (as opposed to zoom's radial pattern or dissolve's near-
    // zero motion).
    public double MinDirectionalConsistency
    {
        get => _minDirectionalConsistency;
        init => _minDirectionalConsistency = Ratio.Clamp(value);
    }

    public double MinMotionMagnitude
    {
        get => _minMotionMagnitude;
        init => _minMotionMagnitude = Math.Max(0, value);
    }

    public int MinSustainedSamples { get; init; } = 2;
}

internal static class Ratio
{
    public static double Clamp(double value) => Math.Clamp(value, 0.0, 1.0);
}
