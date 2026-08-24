namespace SceneForge.Accuracy.Evaluation;

// Passed reflects CorrectnessFailures only - PerformanceNotes is always
// populated when a resource/throughput number moved, but never makes
// Passed false. This is the concrete mechanism behind "CI fails only on
// stable correctness regressions, never on noisy speed variance."
public sealed record RegressionGateResult(
    bool Passed,
    IReadOnlyList<string> CorrectnessFailures,
    IReadOnlyList<string> PerformanceNotes);
