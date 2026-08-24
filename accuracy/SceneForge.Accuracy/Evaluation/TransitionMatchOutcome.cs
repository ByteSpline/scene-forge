namespace SceneForge.Accuracy.Evaluation;

// One outcome per expected transition in a fixture's ground truth: did some
// detection overlap it (TruePositive), and if so, how far was the
// detector's BoundaryTimestamp from the ground-truth window's midpoint
// (BoundaryErrorMs). BoundaryErrorMs is null whenever TruePositive is
// false - there is nothing to measure a boundary error against.
public sealed record TransitionMatchOutcome(bool TruePositive, double? BoundaryErrorMs);
