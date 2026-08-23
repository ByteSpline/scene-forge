using SceneForge.Media.Domain;
using SceneForge.Media.Extraction.Intervals;

namespace SceneForge.Media.Tests.Extraction.Intervals;

public class ExclusionDistanceCalculatorTests
{
    private static TimeRange Range(double startSeconds, double endSeconds) =>
        new(TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds));

    [Fact]
    public void NearestDistance_NoExclusions_ReturnsSentinelDistance()
    {
        var distance = ExclusionDistanceCalculator.NearestDistance(Range(0, 5), []);

        Assert.Equal(ExclusionDistanceCalculator.NoExclusionsDistance, distance);
    }

    [Fact]
    public void NearestDistance_ExclusionBeforeCandidate_ReturnsGapFromExclusionEnd()
    {
        var distance = ExclusionDistanceCalculator.NearestDistance(Range(10, 15), [Range(0, 8)]);

        Assert.Equal(TimeSpan.FromSeconds(2), distance);
    }

    [Fact]
    public void NearestDistance_ExclusionAfterCandidate_ReturnsGapFromExclusionStart()
    {
        var distance = ExclusionDistanceCalculator.NearestDistance(Range(0, 5), [Range(8, 12)]);

        Assert.Equal(TimeSpan.FromSeconds(3), distance);
    }

    [Fact]
    public void NearestDistance_MultipleExclusions_ReturnsTheSmallestGap()
    {
        var distance = ExclusionDistanceCalculator.NearestDistance(Range(10, 15), [Range(0, 8), Range(16, 20)]);

        Assert.Equal(TimeSpan.FromSeconds(1), distance);
    }

    [Fact]
    public void NearestDistance_ExclusionTouchingCandidate_ReturnsZero()
    {
        var distance = ExclusionDistanceCalculator.NearestDistance(Range(5, 10), [Range(0, 5)]);

        Assert.Equal(TimeSpan.Zero, distance);
    }
}
