using System.Text.Json.Serialization;
using SceneForge.Media.Sampling;

namespace SceneForge.Accuracy.Evaluation;

// The full output of one `evaluate` run: correctness (Metrics, one row per
// FixtureGroup plus one aggregate row with Group == null) and resource/
// throughput numbers side by side. CLAUDE.md rule 10 (measured, never
// absolute) is why every field here is exactly what was observed on one
// run, nothing extrapolated or rounded up.
public sealed record EvaluationReport(
    DateTimeOffset CapturedAtUtc,
    string? CommitSha,
    AnalysisProfile Profile,
    int FixtureCount,
    IReadOnlyList<GroupMetrics> Metrics,
    double ThroughputSourceSecondsPerWallClockSecond,
    double TotalWallClockSeconds,
    long PeakManagedMemoryBytes,
    long PeakWorkingSetBytes)
{
    [JsonIgnore]
    public GroupMetrics Aggregate => Metrics.Single(m => m.Group is null);
}
