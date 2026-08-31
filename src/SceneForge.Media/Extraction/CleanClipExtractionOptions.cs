using SceneForge.Media.Domain;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Extraction;

// Every knob CleanClipExtractor needs for one run: how frames are sampled
// (reuses Sampling.FrameSamplingOptions verbatim, same as
// TransitionDetectionOptions), the caller-supplied scene ranges and
// exclusion intervals to subtract from them, and the scoring/clustering
// profiles. SceneRanges/ExcludedIntervals are facts handed in by the
// caller - CleanClipExtractor never computes scene boundaries or detects
// black/frozen/invalid footage itself (clean architecture: those are other
// components' responsibilities).
public sealed record CleanClipExtractionOptions
{
    public required FrameSamplingOptions SamplingOptions { get; init; }

    public required IReadOnlyList<TimeRange> SceneRanges { get; init; }

    public IReadOnlyList<ExcludedInterval> ExcludedIntervals { get; init; } = [];

    public CleanClipScoringOptions Scoring { get; init; } = CleanClipScoringOptions.Default;

    public ClusteringOptions Clustering { get; init; } = ClusteringOptions.Default;

    public static CleanClipExtractionOptions ForProfile(
        AnalysisProfile analysisProfile,
        IReadOnlyList<TimeRange> sceneRanges,
        IReadOnlyList<ExcludedInterval>? excludedIntervals = null) =>
        new()
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(analysisProfile),
            SceneRanges = sceneRanges,
            ExcludedIntervals = excludedIntervals ?? [],
        };
}

// Thresholds and weights driving candidate generation and scoring. Every
// field is clamped in its init setter (same pattern as
// TransitionDetectionProfile) so a misconfigured caller can never produce
// negative durations, out-of-range ratios, or a zero-weight scorer that
// silently ignores every factor.
//
// MinClipDuration and TransitionSafeDistance were recalibrated (see
// docs/CLEAN_CLIP_RETENTION_AUDIT.md) after a real-footage report of only
// 18-19 clips surviving from a 320-scene source - traced to
// TransitionSafeDistance's old default (2s) requiring a candidate be at
// least MinAcceptableFactorScore (0.3) x TransitionSafeDistance = 600ms
// from the nearest exclusion just to avoid automatic rejection on that one
// factor alone, while BoundaryGuard (250ms) only ever pushed a candidate
// 250ms away - so no scene shorter than ~8.5s could ever produce an
// accepted clip, regardless of footage quality (verified directly against
// the real ClipCandidateGenerator/ClipScorer pipeline). The correctness
// guarantee itself (never include a contaminated frame) is unaffected by
// either value - it is enforced structurally by IntervalSubtractor, before
// either of these fields is even consulted; both are extra margin on top
// of that guarantee, not the guarantee. Every other value here remains a
// deliberately heuristic starting point, not calibrated against measured
// real-footage data (CLAUDE.md rule 10) - see docs/PHASE_07_REPORT.md.
public sealed record CleanClipScoringOptions
{
    private static readonly TimeSpan MinAllowedClipDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxAllowedClipDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxAllowedBoundaryGuard = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxAllowedTransitionSafeDistance = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _minClipDuration = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _maxClipDuration = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _boundaryGuard = TimeSpan.FromMilliseconds(250);
    private readonly double _overlapFraction = 0.5;
    private readonly double _sharpnessReferenceValue = 50.0;
    private readonly double _stabilityReferenceValue = 0.05;
    private readonly double _freezeNearZeroThreshold = 0.002;
    private readonly double _freezeRiskRejectionThreshold = 0.5;
    private readonly TimeSpan _transitionSafeDistance = TimeSpan.FromSeconds(0.5);
    private readonly double _overlayRatioReference = 3.0;
    private readonly double _overlaySuspicionRejectionThreshold = 0.7;
    private readonly double _acceptanceThreshold = 0.5;
    private readonly double _minAcceptableFactorScore = 0.3;
    private readonly double _durationWeight = 1.0;
    private readonly double _sharpnessWeight = 1.0;
    private readonly double _stabilityWeight = 1.0;
    private readonly double _exposureWeight = 1.0;
    private readonly double _freezeRiskWeight = 1.0;
    private readonly double _transitionDistanceWeight = 1.0;
    private readonly double _overlaySuspicionWeight = 1.0;

    // Shortest candidate clip generated. A remaining (post-subtraction,
    // post-guard) range shorter than this yields no candidate at all.
    // Lowered from 3s to 2s (see the class-level remarks) - a real clean
    // segment between two closely-spaced transitions should not be
    // discarded outright just because it cannot fill a longer window.
    public TimeSpan MinClipDuration
    {
        get => _minClipDuration;
        init => _minClipDuration = Clamp(value, MinAllowedClipDuration, MaxAllowedClipDuration);
    }

    // Longest candidate clip generated.
    public TimeSpan MaxClipDuration
    {
        get => _maxClipDuration;
        init => _maxClipDuration = Clamp(value, MinAllowedClipDuration, MaxAllowedClipDuration);
    }

    // Safety margin trimmed off both ends of every remaining range before
    // any candidate is generated - absorbs residual jitter/soft edges right
    // at an exclusion boundary that the exclusion interval itself may not
    // have captured exactly.
    public TimeSpan BoundaryGuard
    {
        get => _boundaryGuard;
        init => _boundaryGuard = Clamp(value, TimeSpan.Zero, MaxAllowedBoundaryGuard);
    }

    // Fraction of clip duration successive sliding-window candidates
    // overlap by, within one remaining range - 0 means back-to-back,
    // non-overlapping candidates; higher values generate more (overlapping)
    // candidates for the scorer to choose among.
    public double OverlapFraction
    {
        get => _overlapFraction;
        init => _overlapFraction = Math.Clamp(value, 0.0, 0.9);
    }

    // Mean Laplacian variance considered "fully sharp" (Sharpness score of
    // 1.0); scales linearly below that.
    public double SharpnessReferenceValue
    {
        get => _sharpnessReferenceValue;
        init => _sharpnessReferenceValue = Math.Max(1e-6, value);
    }

    // Mean frame-to-frame structural difference considered "fully
    // unstable" (Stability score of 0.0); scales linearly below that.
    public double StabilityReferenceValue
    {
        get => _stabilityReferenceValue;
        init => _stabilityReferenceValue = Math.Max(1e-6, value);
    }

    // Structural difference below which two consecutive sampled frames are
    // considered suspiciously near-identical for FreezeRisk purposes.
    public double FreezeNearZeroThreshold
    {
        get => _freezeNearZeroThreshold;
        init => _freezeNearZeroThreshold = Math.Clamp(value, 0.0, 1.0);
    }

    // FreezeRisk above this value fails that factor outright, regardless of
    // Overall.
    public double FreezeRiskRejectionThreshold
    {
        get => _freezeRiskRejectionThreshold;
        init => _freezeRiskRejectionThreshold = Math.Clamp(value, 0.0, 1.0);
    }

    // Gap from the nearest excluded interval considered "fully safe"
    // (TransitionDistance score of 1.0); scales linearly below that.
    // Lowered from 2s to 0.5s (see the class-level remarks) to match
    // SyntheticFixtureCatalog's own real transition contamination width
    // (TransitionDuration = 0.5s, the ground truth docs/ACCURACY_REPORT.md
    // is itself measured against) plus TransitionDetectionProfile's
    // PreBuffer/PostBuffer (100ms each) already folded into every excluded
    // interval - 2s asked for four times that width of extra clearance
    // with no supporting evidence.
    public TimeSpan TransitionSafeDistance
    {
        get => _transitionSafeDistance;
        init => _transitionSafeDistance = Clamp(value, TimeSpan.Zero, MaxAllowedTransitionSafeDistance);
    }

    // Border-to-interior edge-density ratio considered maximally suspicious
    // (OverlaySuspicion score of 1.0); a ratio of 1.0 (equal density) scores 0.
    public double OverlayRatioReference
    {
        get => _overlayRatioReference;
        init => _overlayRatioReference = Math.Max(1.0 + 1e-6, value);
    }

    // OverlaySuspicion above this value fails that factor outright,
    // regardless of Overall.
    public double OverlaySuspicionRejectionThreshold
    {
        get => _overlaySuspicionRejectionThreshold;
        init => _overlaySuspicionRejectionThreshold = Math.Clamp(value, 0.0, 1.0);
    }

    // Minimum weighted-average Overall score required to accept a
    // candidate, on top of every individual factor's own threshold.
    public double AcceptanceThreshold
    {
        get => _acceptanceThreshold;
        init => _acceptanceThreshold = Math.Clamp(value, 0.0, 1.0);
    }

    // Shared minimum normalized-goodness threshold for Sharpness,
    // Stability, Exposure, and TransitionDistance below which that
    // individual factor fails on its own, regardless of Overall. FreezeRisk
    // and OverlaySuspicion have their own dedicated rejection thresholds
    // above since they are higher-is-worse rather than higher-is-better.
    public double MinAcceptableFactorScore
    {
        get => _minAcceptableFactorScore;
        init => _minAcceptableFactorScore = Math.Clamp(value, 0.0, 1.0);
    }

    public double DurationWeight
    {
        get => _durationWeight;
        init => _durationWeight = Math.Max(0.0, value);
    }

    public double SharpnessWeight
    {
        get => _sharpnessWeight;
        init => _sharpnessWeight = Math.Max(0.0, value);
    }

    public double StabilityWeight
    {
        get => _stabilityWeight;
        init => _stabilityWeight = Math.Max(0.0, value);
    }

    public double ExposureWeight
    {
        get => _exposureWeight;
        init => _exposureWeight = Math.Max(0.0, value);
    }

    public double FreezeRiskWeight
    {
        get => _freezeRiskWeight;
        init => _freezeRiskWeight = Math.Max(0.0, value);
    }

    public double TransitionDistanceWeight
    {
        get => _transitionDistanceWeight;
        init => _transitionDistanceWeight = Math.Max(0.0, value);
    }

    public double OverlaySuspicionWeight
    {
        get => _overlaySuspicionWeight;
        init => _overlaySuspicionWeight = Math.Max(0.0, value);
    }

    public static CleanClipScoringOptions Default { get; } = new();

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;
}

// Thresholds driving VisualClusterer's greedy, no-ML-model grouping of
// accepted clips by perceptual similarity.
public sealed record ClusteringOptions
{
    private readonly double _hashWeight = 0.5;
    private readonly double _colorHistogramWeight = 0.3;
    private readonly double _edgeHistogramWeight = 0.2;
    private readonly double _motionMismatchPenalty = 0.1;
    private readonly double _similarityThreshold = 0.15;

    // Weight of normalized pHash Hamming distance in the combined
    // perceptual distance.
    public double HashWeight
    {
        get => _hashWeight;
        init => _hashWeight = Math.Max(0.0, value);
    }

    public double ColorHistogramWeight
    {
        get => _colorHistogramWeight;
        init => _colorHistogramWeight = Math.Max(0.0, value);
    }

    public double EdgeHistogramWeight
    {
        get => _edgeHistogramWeight;
        init => _edgeHistogramWeight = Math.Max(0.0, value);
    }

    // Flat penalty added to the combined distance when two descriptors'
    // MotionClass differ.
    public double MotionMismatchPenalty
    {
        get => _motionMismatchPenalty;
        init => _motionMismatchPenalty = Math.Clamp(value, 0.0, 1.0);
    }

    // Two descriptors join the same cluster when their combined distance is
    // at or below this value.
    public double SimilarityThreshold
    {
        get => _similarityThreshold;
        init => _similarityThreshold = Math.Clamp(value, 0.0, 1.0);
    }

    public static ClusteringOptions Default { get; } = new();
}
