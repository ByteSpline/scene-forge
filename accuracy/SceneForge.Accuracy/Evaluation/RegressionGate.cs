namespace SceneForge.Accuracy.Evaluation;

// Compares a fresh EvaluationReport against a committed RegressionBaseline.
// Every metric MetricsCalculator produces is deterministic given the same
// code and the same (deterministically ffmpeg-generated) fixtures - no
// randomness anywhere in TransitionDetector's pipeline - so correctness
// metrics are compared with a small, fixed epsilon that only absorbs
// floating-point noise, and any regression beyond it is a hard failure.
// Resource/throughput numbers are never gated - CPU load, thermal
// throttling, and background processes make them vary run to run on the
// very same code and machine, so a change there can never by itself mean
// the code got worse. This is the whole mechanism behind "CI fails only on
// stable correctness regressions, never on noisy speed variance."
public static class RegressionGate
{
    private const double RateEpsilon = 1e-6;
    private const double BoundaryErrorEpsilonMs = 0.5;
    private const double PerformanceNoteThresholdPercent = 1.0;

    public static RegressionGateResult Evaluate(EvaluationReport current, RegressionBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);

        var correctnessFailures = new List<string>();

        // Nullable<FixtureGroup> (the aggregate row's Group is null) cannot
        // itself be used as a Dictionary key under the `notnull` generic
        // constraint, so the aggregate row is compared directly and every
        // per-group row is keyed by its non-nullable FixtureGroup.
        var baselineByGroup = new Dictionary<Fixtures.FixtureGroup, GroupMetrics>();
        foreach (var metrics in baseline.Report.Metrics)
        {
            if (metrics.Group is { } group)
            {
                baselineByGroup[group] = metrics;
            }
        }

        CompareOne("Aggregate", current.Aggregate, baseline.Report.Aggregate, correctnessFailures);

        foreach (var currentGroup in current.Metrics)
        {
            if (currentGroup.Group is not { } group)
            {
                continue;
            }

            // A group with no baseline counterpart yet (e.g. a fixture
            // group added after the baseline was last captured) has
            // nothing to regress against - skip rather than throw or
            // fabricate a comparison.
            if (!baselineByGroup.TryGetValue(group, out var baselineGroup))
            {
                continue;
            }

            CompareOne(group.ToString(), currentGroup, baselineGroup, correctnessFailures);
        }

        var performanceNotes = new List<string>();
        AddPerformanceNote(performanceNotes, "PeakManagedMemoryBytes", current.PeakManagedMemoryBytes, baseline.Report.PeakManagedMemoryBytes);
        AddPerformanceNote(performanceNotes, "PeakWorkingSetBytes", current.PeakWorkingSetBytes, baseline.Report.PeakWorkingSetBytes);
        AddPerformanceNote(performanceNotes, "ThroughputSourceSecondsPerWallClockSecond", current.ThroughputSourceSecondsPerWallClockSecond, baseline.Report.ThroughputSourceSecondsPerWallClockSecond);

        return new RegressionGateResult(correctnessFailures.Count == 0, correctnessFailures, performanceNotes);
    }

    private static void CompareOne(string label, GroupMetrics current, GroupMetrics baseline, List<string> failures)
    {
        CheckHigherIsBetter(label, "Recall", current.Recall, baseline.Recall, failures);
        CheckHigherIsBetter(label, "Precision", current.Precision, baseline.Precision, failures);
        CheckHigherIsBetter(label, "F1", current.F1, baseline.F1, failures);
        CheckLowerIsBetter(label, "BoundaryErrorMs", current.MeanBoundaryErrorMs, baseline.MeanBoundaryErrorMs, BoundaryErrorEpsilonMs, failures);
        CheckLowerIsBetter(label, "FalsePositivesPerMinute", current.FalsePositivesPerMinute, baseline.FalsePositivesPerMinute, RateEpsilon, failures);
    }

    private static void CheckHigherIsBetter(string label, string metricName, double current, double baseline, List<string> failures)
    {
        // A NaN baseline means "not applicable" (e.g. a distractor group's
        // Recall) and can never regress - there was nothing to have gotten
        // worse from. Only a defined baseline value that drops, or
        // disappears entirely (current becomes NaN), counts.
        if (double.IsNaN(baseline))
        {
            return;
        }

        if (double.IsNaN(current) || current < baseline - RateEpsilon)
        {
            failures.Add($"{label}.{metricName} regressed: {Describe(current)} < baseline {baseline:F4}.");
        }
    }

    private static void CheckLowerIsBetter(string label, string metricName, double current, double baseline, double epsilon, List<string> failures)
    {
        if (double.IsNaN(baseline) || double.IsNaN(current))
        {
            return;
        }

        if (current > baseline + epsilon)
        {
            failures.Add($"{label}.{metricName} regressed: {current:F4} > baseline {baseline:F4} (+{epsilon} tolerance).");
        }
    }

    private static void AddPerformanceNote(List<string> notes, string metricName, double current, double baseline)
    {
        if (baseline == 0)
        {
            return;
        }

        var percentChange = (current - baseline) / baseline * 100.0;
        if (Math.Abs(percentChange) < PerformanceNoteThresholdPercent)
        {
            return;
        }

        var sign = percentChange >= 0 ? "+" : string.Empty;
        notes.Add($"{metricName}: {current:F0} vs baseline {baseline:F0} ({sign}{percentChange:F1}% - informational only, never gates CI).");
    }

    private static string Describe(double value) => double.IsNaN(value) ? "NaN (no longer detected)" : value.ToString("F4");
}
