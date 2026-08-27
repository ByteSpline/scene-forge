namespace SceneForge.Media.Planning;

// Which situation produced a TimelineFeasibilityWarning - the two are very
// different for a caller/UI to act on, so they are never collapsed into one
// undifferentiated warning string.
public enum TimelineFeasibilityWarningKind
{
    // The available footage - even after relaxing every relaxable placement
    // constraint (see RelaxedConstraint) and raising MaximumReuseCount to
    // the smallest value TimelinePlanner could prove sufficient - still
    // could not reach QuantizedTargetDuration. Only reachable when the pool
    // has no usable positive-duration content at all (empty, or every clip
    // has collapsed to zero duration): TimelinePlan.IsComplete is false.
    Shortfall,

    // QuantizedTargetDuration was reached exactly, but only by placing at
    // least one clip more times than TimelinePlanRequest.MaximumReuseCount
    // requested. Shown for transparency only - TimelinePlan.IsComplete is
    // still true, and this never blocks or shortens the plan.
    SignificantRepetition,
}

// Set on TimelinePlan whenever something about reaching QuantizedTargetDuration
// required relaxing the caller's requested constraints further than plain
// spacing relaxation alone (see RelaxedConstraint). Quantified, never a
// silent short plan and never a silently heavy-repetition one either:
// CLAUDE.md rule 10 forbids treating "ran out of usable footage" as an
// absolute failure to hide, and the product requirement behind this type is
// that a duration shortfall must never be papered over by simply returning
// less video than requested - MaximumReuseCount bends before the target
// duration does (see TimelinePlanRequest.MaximumReuseCount).
public sealed record TimelineFeasibilityWarning
{
    public required TimelineFeasibilityWarningKind Kind { get; init; }

    // Human-readable summary including every number below, e.g. "Requested
    // 120.00s but only 87.50s is achievable from 14 clip(s) even after
    // relaxing the maximum reuse count to 40 (from a requested 2) and every
    // placement-spacing constraint (shortfall 32.50s)." or "Target duration
    // 1320.00s was reached exactly, but only by allowing clips to repeat up
    // to 9 time(s) - 8 more than the requested maximum of 1 - because 180
    // clip(s) were not enough to cover it otherwise. Significant repetition
    // was needed to match audio length."
    public required string Message { get; init; }

    public required TimeSpan TargetDuration { get; init; }

    public required TimeSpan AchievedDuration { get; init; }

    // TargetDuration - AchievedDuration for Kind == Shortfall; always
    // TimeSpan.Zero for Kind == SignificantRepetition, since the target was
    // actually reached.
    public required TimeSpan Shortfall { get; init; }

    // TimelinePlanRequest.MaximumReuseCount as the caller originally
    // requested it.
    public required int RequestedMaximumReuseCount { get; init; }

    // The reuse cap TimelinePlanner actually planned against to produce
    // this result - equal to RequestedMaximumReuseCount unless reuse
    // relaxation was attempted (Kind == SignificantRepetition, or Kind ==
    // Shortfall after relaxation still proved insufficient).
    public required int EffectiveMaximumReuseCount { get; init; }
}
