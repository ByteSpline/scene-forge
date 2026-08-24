using SceneForge.Accuracy.Evaluation;
using SceneForge.Accuracy.Fixtures;

namespace SceneForge.Accuracy.Tests;

public class MetricsCalculatorTests
{
    [Fact]
    public void Compute_AllMatchedNoFalsePositives_ReportsPerfectRecallAndPrecision()
    {
        var input = new GroupEvaluationInput(
            FixtureGroup.HardCut,
            [new TransitionMatchOutcome(true, 12.0), new TransitionMatchOutcome(true, 8.0)],
            FalsePositiveCount: 0,
            TotalSourceSeconds: 6.0);

        var results = MetricsCalculator.Compute([input]);
        var hardCut = Single(results, FixtureGroup.HardCut);

        Assert.Equal(2, hardCut.TruePositives);
        Assert.Equal(0, hardCut.FalseNegatives);
        Assert.Equal(0, hardCut.FalsePositives);
        Assert.Equal(1.0, hardCut.Recall);
        Assert.Equal(1.0, hardCut.Precision);
        Assert.Equal(1.0, hardCut.F1);
        Assert.Equal(10.0, hardCut.MeanBoundaryErrorMs);
        Assert.Equal(0.0, hardCut.FalsePositivesPerMinute);
    }

    [Fact]
    public void Compute_OneMissedOneFalsePositive_ReportsPartialRecallAndPrecision()
    {
        var input = new GroupEvaluationInput(
            FixtureGroup.Dissolve,
            [new TransitionMatchOutcome(true, 20.0), new TransitionMatchOutcome(false, null)],
            FalsePositiveCount: 1,
            TotalSourceSeconds: 30.0);

        var results = MetricsCalculator.Compute([input]);
        var dissolve = Single(results, FixtureGroup.Dissolve);

        Assert.Equal(1, dissolve.TruePositives);
        Assert.Equal(1, dissolve.FalseNegatives);
        Assert.Equal(1, dissolve.FalsePositives);
        Assert.Equal(0.5, dissolve.Recall);
        Assert.Equal(0.5, dissolve.Precision);
        Assert.Equal(0.5, dissolve.F1);
        Assert.Equal(20.0, dissolve.MeanBoundaryErrorMs);
        // 1 false positive over 30s = 2 per minute.
        Assert.Equal(2.0, dissolve.FalsePositivesPerMinute);
    }

    [Fact]
    public void Compute_DistractorGroupWithNoFalsePositives_RecallAndPrecisionAreNotApplicable()
    {
        var input = new GroupEvaluationInput(
            FixtureGroup.StaticShot,
            Matches: [],
            FalsePositiveCount: 0,
            TotalSourceSeconds: 60.0);

        var results = MetricsCalculator.Compute([input]);
        var staticShot = Single(results, FixtureGroup.StaticShot);

        Assert.Equal(0, staticShot.TruePositives);
        Assert.Equal(0, staticShot.FalseNegatives);
        Assert.Equal(0, staticShot.FalsePositives);
        Assert.True(double.IsNaN(staticShot.Recall));
        Assert.True(double.IsNaN(staticShot.Precision));
        Assert.True(double.IsNaN(staticShot.F1));
        Assert.True(double.IsNaN(staticShot.MeanBoundaryErrorMs));
        Assert.Equal(0.0, staticShot.FalsePositivesPerMinute);
    }

    [Fact]
    public void Compute_DistractorGroupWithFalsePositives_PrecisionIsZeroNotNaN()
    {
        var input = new GroupEvaluationInput(
            FixtureGroup.RapidMotion,
            Matches: [],
            FalsePositiveCount: 3,
            TotalSourceSeconds: 30.0);

        var results = MetricsCalculator.Compute([input]);
        var rapidMotion = Single(results, FixtureGroup.RapidMotion);

        Assert.True(double.IsNaN(rapidMotion.Recall), "Recall stays not-applicable: there is nothing to recall.");
        Assert.Equal(0.0, rapidMotion.Precision);
        Assert.True(double.IsNaN(rapidMotion.F1), "F1 needs a real Recall to combine with Precision.");
        // 3 false positives over 30s = 6 per minute.
        Assert.Equal(6.0, rapidMotion.FalsePositivesPerMinute);
    }

    [Fact]
    public void Compute_MultipleGroups_AlsoReturnsAggregateAcrossAllGroups()
    {
        var hardCut = new GroupEvaluationInput(
            FixtureGroup.HardCut,
            [new TransitionMatchOutcome(true, 10.0)],
            FalsePositiveCount: 0,
            TotalSourceSeconds: 30.0);
        var blackHold = new GroupEvaluationInput(
            FixtureGroup.BlackHold,
            Matches: [],
            FalsePositiveCount: 1,
            TotalSourceSeconds: 30.0);

        var results = MetricsCalculator.Compute([hardCut, blackHold]);

        Assert.Equal(3, results.Count);
        var aggregate = Assert.Single(results, r => r.Group is null);

        Assert.Equal(1, aggregate.TruePositives);
        Assert.Equal(0, aggregate.FalseNegatives);
        Assert.Equal(1, aggregate.FalsePositives);
        // Aggregate recall is over expected transitions only (the one from
        // HardCut) - BlackHold contributes zero expected transitions.
        Assert.Equal(1.0, aggregate.Recall);
        Assert.Equal(0.5, aggregate.Precision);
        // 1 false positive over 60s combined duration = 1 per minute.
        Assert.Equal(1.0, aggregate.FalsePositivesPerMinute);
    }

    [Fact]
    public void Compute_EmptyInput_ReturnsOnlyAnAggregateOfNothing()
    {
        var results = MetricsCalculator.Compute([]);

        var aggregate = Assert.Single(results);
        Assert.Null(aggregate.Group);
        Assert.Equal(0, aggregate.TruePositives);
        Assert.True(double.IsNaN(aggregate.Recall));
        Assert.True(double.IsNaN(aggregate.FalsePositivesPerMinute), "Zero total source seconds makes a per-minute rate not applicable.");
    }

    private static GroupMetrics Single(IReadOnlyList<GroupMetrics> results, FixtureGroup group) =>
        Assert.Single(results, r => r.Group == group);
}
