namespace SceneForge.Media.Planning;

// The full result of one TimelinePlanner.Plan run - the plan itself
// (Placements), why every placement happened (DecisionTrace, one entry per
// placement, CLAUDE.md rule 10: never opaque), and, when footage ran short,
// a quantified explanation rather than a silently-incomplete plan
// (FeasibilityWarning).
public sealed record TimelinePlan
{
    public required IReadOnlyList<TimelinePlacement> Placements { get; init; }

    // Sum of every placement's UsedDuration.
    public required TimeSpan PlannedDuration { get; init; }

    // TimelinePlanRequest.TargetAudioDuration, unmodified.
    public required TimeSpan TargetDuration { get; init; }

    // TargetDuration rounded to the nearest whole frame count at
    // TimelinePlanRequest.OutputTimeBase - the actual duration
    // TimelinePlanner planned against. See TargetFrameCount/AudioDurationRoundingError.
    public required TimeSpan QuantizedTargetDuration { get; init; }

    // OutputTimeBase.ToFrameCount(TargetDuration) - QuantizedTargetDuration
    // expressed as a frame count, since "matches the audio duration exactly
    // in the selected output time base" (CLAUDE.md-adjacent phase
    // requirement) is a frame-count equality, not a raw TimeSpan one.
    public required long TargetFrameCount { get; init; }

    // QuantizedTargetDuration - TargetDuration - the sub-frame rounding
    // this plan's exactness claim is measured against, never hidden inside
    // an unexplained few-millisecond mismatch (CLAUDE.md rule 10: never
    // claim exactness without stating what "exact" was rounded to).
    public required TimeSpan AudioDurationRoundingError { get; init; }

    // True only when PlannedDuration == QuantizedTargetDuration exactly.
    // False only when the available footage has no usable positive-duration
    // content at all - every other shortfall is closed by relaxing
    // constraints (spacing, then MaximumReuseCount) before this is ever
    // false. FeasibilityWarning can still be non-null when this is true -
    // see its Kind.
    public required bool IsComplete { get; init; }

    // One entry per Placements entry, same order, same Position values -
    // zip the two lists to see both what was placed and why.
    public required IReadOnlyList<TimelinePlanTraceEntry> DecisionTrace { get; init; }

    // Null only when the target was reached without needing to exceed the
    // requested MaximumReuseCount. Non-null and IsComplete both true means
    // the target was reached exactly but only via reuse relaxation
    // (TimelineFeasibilityWarningKind.SignificantRepetition, informational
    // only). Non-null and IsComplete false means the target could not be
    // reached even after relaxing every relaxable constraint
    // (TimelineFeasibilityWarningKind.Shortfall). See TimelineFeasibilityWarning.
    public required TimelineFeasibilityWarning? FeasibilityWarning { get; init; }
}
