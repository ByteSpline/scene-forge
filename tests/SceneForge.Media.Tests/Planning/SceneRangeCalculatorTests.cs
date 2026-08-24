using SceneForge.Media.Detection;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;

namespace SceneForge.Media.Tests.Planning;

public class SceneRangeCalculatorTests
{
    [Fact]
    public void Calculate_NoDetections_ReturnsSingleSceneSpanningWholeDuration()
    {
        var result = SceneRangeCalculator.Calculate(TimeSpan.FromSeconds(30), []);

        var range = Assert.Single(result.SceneRanges);
        Assert.Equal(TimeSpan.Zero, range.Start);
        Assert.Equal(TimeSpan.FromSeconds(30), range.End);
        Assert.Empty(result.ExcludedIntervals);
        var boundary = Assert.Single(result.BoundaryTransitions);
        Assert.Null(boundary.Leading);
        Assert.Null(boundary.Trailing);
    }

    [Fact]
    public void Calculate_OneMidDetection_SplitsIntoTwoScenesWithMatchingBoundaries()
    {
        var detection = CreateDetection(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11), TransitionType.HardCut);

        var result = SceneRangeCalculator.Calculate(TimeSpan.FromSeconds(30), [detection]);

        Assert.Equal(2, result.SceneRanges.Count);
        Assert.Equal(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)), result.SceneRanges[0]);
        Assert.Equal(new TimeRange(TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(30)), result.SceneRanges[1]);

        var exclusion = Assert.Single(result.ExcludedIntervals);
        Assert.Equal(ExclusionKind.Transition, exclusion.Kind);
        Assert.Equal("HardCut", exclusion.Reason);
        Assert.Equal(new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11)), exclusion.Range);

        Assert.Equal(2, result.BoundaryTransitions.Count);
        Assert.Null(result.BoundaryTransitions[0].Leading);
        Assert.Same(detection, result.BoundaryTransitions[0].Trailing);
        Assert.Same(detection, result.BoundaryTransitions[1].Leading);
        Assert.Null(result.BoundaryTransitions[1].Trailing);
    }

    [Fact]
    public void Calculate_DetectionAtVeryStart_ProducesNoLeadingSceneBeforeIt()
    {
        var detection = CreateDetection(TimeSpan.Zero, TimeSpan.FromSeconds(1), TransitionType.FadeFromBlack);

        var result = SceneRangeCalculator.Calculate(TimeSpan.FromSeconds(10), [detection]);

        var range = Assert.Single(result.SceneRanges);
        Assert.Equal(new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)), range);
        var boundary = Assert.Single(result.BoundaryTransitions);
        Assert.Same(detection, boundary.Leading);
        Assert.Null(boundary.Trailing);
    }

    [Fact]
    public void Calculate_DetectionAtVeryEnd_ProducesNoTrailingSceneAfterIt()
    {
        var detection = CreateDetection(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(10), TransitionType.FadeToBlack);

        var result = SceneRangeCalculator.Calculate(TimeSpan.FromSeconds(10), [detection]);

        var range = Assert.Single(result.SceneRanges);
        Assert.Equal(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(9)), range);
        var boundary = Assert.Single(result.BoundaryTransitions);
        Assert.Null(boundary.Leading);
        Assert.Same(detection, boundary.Trailing);
    }

    [Fact]
    public void Calculate_DetectionSpanningEntireDuration_ProducesNoSceneRanges()
    {
        var detection = CreateDetection(TimeSpan.Zero, TimeSpan.FromSeconds(10), TransitionType.Dissolve);

        var result = SceneRangeCalculator.Calculate(TimeSpan.FromSeconds(10), [detection]);

        Assert.Empty(result.SceneRanges);
        Assert.Empty(result.BoundaryTransitions);
        Assert.Single(result.ExcludedIntervals);
    }

    [Fact]
    public void Calculate_DetectionsOutOfOrder_AreSortedBeforeProcessing()
    {
        var second = CreateDetection(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(21), TransitionType.HardCut);
        var first = CreateDetection(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11), TransitionType.HardCut);

        var result = SceneRangeCalculator.Calculate(TimeSpan.FromSeconds(30), [second, first]);

        Assert.Equal(3, result.SceneRanges.Count);
        Assert.Equal(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)), result.SceneRanges[0]);
        Assert.Equal(new TimeRange(TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(20)), result.SceneRanges[1]);
        Assert.Equal(new TimeRange(TimeSpan.FromSeconds(21), TimeSpan.FromSeconds(30)), result.SceneRanges[2]);
    }

    [Fact]
    public void Calculate_OverlappingDetections_MergedCursorNeverMovesBackward()
    {
        var wide = CreateDetection(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TransitionType.Dissolve);
        var containedLater = CreateDetection(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10), TransitionType.HardCut);

        var result = SceneRangeCalculator.Calculate(TimeSpan.FromSeconds(20), [wide, containedLater]);

        Assert.Equal(2, result.ExcludedIntervals.Count);
        Assert.Equal(2, result.SceneRanges.Count);
        Assert.Equal(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)), result.SceneRanges[0]);
        Assert.Equal(new TimeRange(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20)), result.SceneRanges[1]);
        foreach (var range in result.SceneRanges)
        {
            Assert.True(range.Start <= range.End);
        }
    }

    [Fact]
    public void Calculate_DetectionExceedingTotalDuration_IsClampedRatherThanThrowing()
    {
        var detection = CreateDetection(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(15), TransitionType.FadeToBlack);

        var result = SceneRangeCalculator.Calculate(TimeSpan.FromSeconds(10), [detection]);

        var exclusion = Assert.Single(result.ExcludedIntervals);
        Assert.Equal(TimeSpan.FromSeconds(10), exclusion.Range.End);
        var range = Assert.Single(result.SceneRanges);
        Assert.Equal(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(9)), range);
    }

    [Fact]
    public void Calculate_NegativeTotalDuration_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SceneRangeCalculator.Calculate(TimeSpan.FromSeconds(-1), []));
    }

    [Fact]
    public void Calculate_NullDetections_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SceneRangeCalculator.Calculate(TimeSpan.FromSeconds(10), null!));
    }

    private static TransitionDetection CreateDetection(TimeSpan start, TimeSpan end, TransitionType type) =>
        new()
        {
            Type = type,
            Start = start,
            Peak = start + TimeSpan.FromTicks((end - start).Ticks / 2),
            End = end,
            BoundaryTimestamp = start,
            Confidence = 0.9,
            ContributingSignals = new Dictionary<string, double> { ["Test"] = 0.9 },
            DiagnosticReason = "test fixture detection",
        };
}
