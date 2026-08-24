using SceneForge.Accuracy.Fixtures;

namespace SceneForge.Accuracy.Evaluation;

// Raw tallies MetricsCalculator needs for one FixtureGroup: one
// TransitionMatchOutcome per expected transition across every fixture in
// the group (an empty list for a distractor group, which has none),
// however many detections were left over unmatched (false positives), and
// the total source duration analyzed for the group (the denominator for
// false-positives-per-minute).
public sealed record GroupEvaluationInput(
    FixtureGroup Group,
    IReadOnlyList<TransitionMatchOutcome> Matches,
    int FalsePositiveCount,
    double TotalSourceSeconds);
