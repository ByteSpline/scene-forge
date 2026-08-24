using SceneForge.Accuracy.Fixtures;

namespace SceneForge.Accuracy.Evaluation;

// Group is null exactly once per report: the aggregate row computed over
// every group's matches/false-positives/duration combined. CLAUDE.md rule
// 10 applies throughout - a NaN here is a deliberate "not applicable" (e.g.
// Recall is NaN for a distractor group, which has no expected transitions
// to recall), never a stand-in for zero or a hidden failure.
public sealed record GroupMetrics(
    FixtureGroup? Group,
    int TruePositives,
    int FalseNegatives,
    int FalsePositives,
    double Recall,
    double Precision,
    double F1,
    double MeanBoundaryErrorMs,
    double FalsePositivesPerMinute);
