using SceneForge.Accuracy.Evaluation;
using SceneForge.Accuracy.Fixtures;
using SceneForge.Media.Sampling;

namespace SceneForge.Accuracy.Tests;

public class RegressionGateTests
{
    [Fact]
    public void Evaluate_IdenticalReport_Passes()
    {
        var baseline = Baseline(HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.0, fpPerMinute: 0.0));
        var current = Report(HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.0, fpPerMinute: 0.0));

        var result = RegressionGate.Evaluate(current, baseline);

        Assert.True(result.Passed);
        Assert.Empty(result.CorrectnessFailures);
    }

    [Fact]
    public void Evaluate_RecallDropsBeyondEpsilon_Fails()
    {
        var baseline = Baseline(HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.0, fpPerMinute: 0.0));
        var current = Report(HardCutMetrics(recall: 0.5, precision: 1.0, f1: 0.67, boundaryErrorMs: 10.0, fpPerMinute: 0.0));

        var result = RegressionGate.Evaluate(current, baseline);

        Assert.False(result.Passed);
        Assert.Contains(result.CorrectnessFailures, f => f.Contains("Recall", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_BoundaryErrorGrowsBeyondEpsilon_Fails()
    {
        var baseline = Baseline(HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.0, fpPerMinute: 0.0));
        var current = Report(HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 40.0, fpPerMinute: 0.0));

        var result = RegressionGate.Evaluate(current, baseline);

        Assert.False(result.Passed);
        Assert.Contains(result.CorrectnessFailures, f => f.Contains("BoundaryError", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_FalsePositivesPerMinuteGrowsBeyondEpsilon_Fails()
    {
        var baseline = Baseline(HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.0, fpPerMinute: 0.0));
        var current = Report(HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.0, fpPerMinute: 4.0));

        var result = RegressionGate.Evaluate(current, baseline);

        Assert.False(result.Passed);
        Assert.Contains(result.CorrectnessFailures, f => f.Contains("FalsePositivesPerMinute", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_TinyFloatingPointNoiseWithinEpsilon_Passes()
    {
        var baseline = Baseline(HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.000, fpPerMinute: 0.0));
        var current = Report(HardCutMetrics(recall: 1.0 - 1e-9, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.001, fpPerMinute: 0.0));

        var result = RegressionGate.Evaluate(current, baseline);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Evaluate_LargePerformanceRegressionAlone_NeverFailsTheGate()
    {
        var baseline = Baseline(HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.0, fpPerMinute: 0.0), peakMemory: 10_000_000, throughput: 20.0);
        var current = Report(HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.0, fpPerMinute: 0.0), peakMemory: 500_000_000, throughput: 0.5);

        var result = RegressionGate.Evaluate(current, baseline);

        Assert.True(result.Passed, "A 50x memory increase and 40x slowdown must never fail the gate - only correctness regressions do.");
        Assert.NotEmpty(result.PerformanceNotes);
    }

    [Fact]
    public void Evaluate_GroupAbsentFromBaseline_IsSkippedRatherThanThrowing()
    {
        var baseline = Baseline(HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.0, fpPerMinute: 0.0));
        var current = Report(
            HardCutMetrics(recall: 1.0, precision: 1.0, f1: 1.0, boundaryErrorMs: 10.0, fpPerMinute: 0.0),
            new GroupMetrics(FixtureGroup.RapidMotion, 0, 0, 2, double.NaN, 0.0, double.NaN, double.NaN, 4.0));

        var result = RegressionGate.Evaluate(current, baseline);

        Assert.True(result.Passed);
    }

    private static GroupMetrics HardCutMetrics(double recall, double precision, double f1, double boundaryErrorMs, double fpPerMinute) =>
        new(FixtureGroup.HardCut, 2, 0, 0, recall, precision, f1, boundaryErrorMs, fpPerMinute);

    private static EvaluationReport Report(GroupMetrics group, params GroupMetrics[] extra)
    {
        var metrics = new List<GroupMetrics> { group };
        metrics.AddRange(extra);
        metrics.Add(group with { Group = null }); // aggregate row

        return new EvaluationReport(
            DateTimeOffset.UtcNow,
            CommitSha: "deadbeef",
            AnalysisProfile.Accurate,
            FixtureCount: 2,
            Metrics: metrics,
            ThroughputSourceSecondsPerWallClockSecond: 10.0,
            TotalWallClockSeconds: 1.0,
            PeakManagedMemoryBytes: 1_000_000,
            PeakWorkingSetBytes: 2_000_000);
    }

    private static EvaluationReport Report(GroupMetrics group, long peakMemory, double throughput) =>
        Report(group) with { PeakManagedMemoryBytes = peakMemory, ThroughputSourceSecondsPerWallClockSecond = throughput };

    private static RegressionBaseline Baseline(GroupMetrics group) =>
        new(SampleHardware(), Report(group));

    private static RegressionBaseline Baseline(GroupMetrics group, long peakMemory, double throughput) =>
        new(SampleHardware(), Report(group, peakMemory, throughput));

    private static HardwareDescription SampleHardware() =>
        new("Test CPU", 8, 16.0, "Windows 10", "8.0.424");
}
