namespace SceneForge.Media.Planning;

// One constraint TimelinePlanner had to relax to keep making progress
// toward TargetAudioDuration, or to honor the exact-duration-match
// requirement despite a duration-bounds preference. Always paired with the
// specific placement it affected in TimelinePlanTraceEntry.RelaxedConstraints
// - never a silent fallback (CLAUDE.md rule 10).
public enum RelaxedConstraint
{
    // TimelinePlanRequest.VisualClusterAdjacencyLimit could not be honored
    // for this placement - relaxed first, since a repeated visual cluster
    // is the least disruptive repetition to allow.
    VisualClusterAdjacencyLimit,

    // TimelinePlanRequest.OriginalNeighborSeparation could not be honored -
    // relaxed second.
    OriginalNeighborSeparation,

    // TimelinePlanRequest.MinimumRepeatDistance could not be honored -
    // relaxed third (of the three placement-spacing constraints), since an
    // early repeat of the same clip is the most noticeable repetition.
    MinimumRepeatDistance,

    // This placement's use of its clip exceeded TimelinePlanRequest.MaximumReuseCount
    // as originally requested. Reaching TargetAudioDuration exactly always
    // takes priority over the requested reuse limit (a hard product
    // requirement - never produce a short output when reasonable relaxation
    // can close the gap), so TimelinePlanner re-plans with the smallest
    // reuse cap it can prove sufficient only once every spacing constraint
    // above has already been exhausted at the originally requested cap. See
    // TimelinePlanner's algorithm doc comment and
    // TimelineFeasibilityWarningKind.SignificantRepetition.
    MaximumReuseCount,

    // The final placement's trimmed duration landed below
    // TimelineDurationBounds.MinFinalClipDuration.
    FinalClipBelowMinDuration,

    // The final placement's untrimmed candidate duration overshot the
    // remaining budget by more than TimelineDurationBounds.MaxOvershoot.
    FinalClipOvershootExceeded,
}
