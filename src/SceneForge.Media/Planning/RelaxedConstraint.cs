namespace SceneForge.Media.Planning;

// One constraint TimelinePlanner had to relax to keep making progress
// toward TargetAudioDuration, or to honor the exact-duration-match
// requirement despite a duration-bounds preference. Always paired with the
// specific placement it affected in TimelinePlanTraceEntry.RelaxedConstraints
// - never a silent fallback (CLAUDE.md rule 10). MaximumReuseCount is
// deliberately absent: it is the one hard cap TimelinePlanner never
// relaxes, see TimelinePlan.FeasibilityWarning for what happens instead
// when it makes the target unreachable.
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
    // relaxed last (of the three placement-spacing constraints), since an
    // early repeat of the same clip is the most noticeable repetition.
    MinimumRepeatDistance,

    // The final placement's trimmed duration landed below
    // TimelineDurationBounds.MinFinalClipDuration.
    FinalClipBelowMinDuration,

    // The final placement's untrimmed candidate duration overshot the
    // remaining budget by more than TimelineDurationBounds.MaxOvershoot.
    FinalClipOvershootExceeded,
}
