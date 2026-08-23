namespace SceneForge.Media.Extraction;

// One value per way a candidate clip's score can fail to meet its
// per-factor acceptance threshold, plus the overall-score catch-all. Named
// (not a free-text-only reason) so rejected candidates remain queryable/
// groupable in the JSON export, the same "never opaque" convention
// TransitionDetection.DiagnosticReason/ContributingSignals established for
// Phase 6 - see ScoreReason, which pairs each of these with the specific
// measured value that triggered it.
public enum RejectionReason
{
    DurationOutOfRange,
    InsufficientSharpness,
    UnstableMotion,
    PoorExposure,
    HighFreezeRisk,
    TooCloseToExclusion,
    SuspectedOverlay,
    LowOverallScore,
}
