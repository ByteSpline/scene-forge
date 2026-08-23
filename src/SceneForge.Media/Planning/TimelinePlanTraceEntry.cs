namespace SceneForge.Media.Planning;

// Why one placement happened, explaining acceptance the same way
// ScoreReason explains a CleanClip's acceptance/rejection: never a single
// opaque "here is the plan," always a paired explanation. TimelinePlan.DecisionTrace
// has exactly one entry per TimelinePlan.Placements entry, in the same
// order, so a caller can zip the two lists to see both what was placed and
// why.
public sealed record TimelinePlanTraceEntry
{
    public required int Position { get; init; }

    public required int ClipIndex { get; init; }

    // Human-readable detail: which candidates were considered, the
    // usage-count/shuffle-rank tie-break that selected this one, and
    // whether/how it was trimmed - e.g. "Selected clip 4 (unused so far,
    // shuffle rank 11/40); satisfied all placement constraints at full
    // strictness; trimmed from 4.80s to 2.13s to match the remaining
    // budget exactly."
    public required string Explanation { get; init; }

    // Empty when every constraint was honored at full strictness for this
    // placement. Non-empty entries always correspond 1:1 with a reason
    // named in Explanation - see RelaxedConstraint.
    public required IReadOnlyList<RelaxedConstraint> RelaxedConstraints { get; init; }
}
