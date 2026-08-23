namespace SceneForge.Media.Planning;

// Set on TimelinePlan when the available footage - even after relaxing
// every relaxable placement constraint (see RelaxedConstraint) and using
// every clip up to TimelinePlanRequest.MaximumReuseCount, the one hard cap
// TimelinePlanner never crosses - still could not reach QuantizedTargetDuration.
// Quantified, never a silent short plan: CLAUDE.md rule 10 forbids treating
// "ran out of usable footage" as an absolute failure to hide, and forbids
// papering over it by exceeding MaximumReuseCount into extreme repetition
// instead.
public sealed record TimelineFeasibilityWarning
{
    // Human-readable summary including every number below, e.g. "Requested
    // 120.00s but only 87.50s is achievable from 14 clip(s) at a maximum
    // reuse count of 2 under the active placement constraints (shortfall
    // 32.50s)."
    public required string Message { get; init; }

    public required TimeSpan TargetDuration { get; init; }

    public required TimeSpan AchievedDuration { get; init; }

    // Always TargetDuration - AchievedDuration, so a caller never has to
    // recompute it.
    public required TimeSpan Shortfall { get; init; }
}
