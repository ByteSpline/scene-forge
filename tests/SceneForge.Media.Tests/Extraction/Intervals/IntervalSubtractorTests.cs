using SceneForge.Media.Domain;
using SceneForge.Media.Extraction.Intervals;

namespace SceneForge.Media.Tests.Extraction.Intervals;

public class IntervalSubtractorTests
{
    private static TimeRange Range(double startSeconds, double endSeconds) =>
        new(TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds));

    [Fact]
    public void Subtract_NoExclusions_ReturnsSceneRangeUnchanged()
    {
        var result = IntervalSubtractor.Subtract([Range(0, 10)], []);

        var remainder = Assert.Single(result);
        Assert.Equal(0, remainder.SourceSceneIndex);
        Assert.Equal(Range(0, 10), remainder.Range);
    }

    [Fact]
    public void Subtract_ExclusionInMiddle_SplitsIntoTwoRemainders()
    {
        var result = IntervalSubtractor.Subtract([Range(0, 10)], [Range(4, 6)]);

        Assert.Equal(2, result.Count);
        Assert.Equal(Range(0, 4), result[0].Range);
        Assert.Equal(Range(6, 10), result[1].Range);
        Assert.All(result, r => Assert.Equal(0, r.SourceSceneIndex));
    }

    [Fact]
    public void Subtract_ExclusionCoversWholeSceneRange_ReturnsNoRemainder()
    {
        var result = IntervalSubtractor.Subtract([Range(2, 8)], [Range(0, 10)]);

        Assert.Empty(result);
    }

    [Fact]
    public void Subtract_ExclusionOutsideSceneRange_HasNoEffect()
    {
        var result = IntervalSubtractor.Subtract([Range(5, 10)], [Range(0, 2)]);

        var remainder = Assert.Single(result);
        Assert.Equal(Range(5, 10), remainder.Range);
    }

    // Off-by-one timing: an exclusion that ends exactly where the scene
    // range begins (or begins exactly where it ends) only touches it and
    // must not consume any of the range, nor produce a zero-length remainder.
    [Fact]
    public void Subtract_ExclusionTouchingSceneStart_ConsumesNothing()
    {
        var result = IntervalSubtractor.Subtract([Range(5, 10)], [Range(0, 5)]);

        var remainder = Assert.Single(result);
        Assert.Equal(Range(5, 10), remainder.Range);
    }

    [Fact]
    public void Subtract_ExclusionTouchingSceneEnd_ConsumesNothing()
    {
        var result = IntervalSubtractor.Subtract([Range(0, 5)], [Range(5, 10)]);

        var remainder = Assert.Single(result);
        Assert.Equal(Range(0, 5), remainder.Range);
    }

    // An exclusion that exactly matches the scene range's own boundary
    // (End == sceneRange.End) must not leave a zero-length trailing sliver.
    [Fact]
    public void Subtract_ExclusionEndsExactlyAtSceneEnd_NoZeroLengthRemainder()
    {
        var result = IntervalSubtractor.Subtract([Range(0, 10)], [Range(6, 10)]);

        var remainder = Assert.Single(result);
        Assert.Equal(Range(0, 6), remainder.Range);
        Assert.All(result, r => Assert.True(r.Range.Duration > TimeSpan.Zero));
    }

    [Fact]
    public void Subtract_ExclusionStartsExactlyAtSceneStart_NoZeroLengthRemainder()
    {
        var result = IntervalSubtractor.Subtract([Range(0, 10)], [Range(0, 4)]);

        var remainder = Assert.Single(result);
        Assert.Equal(Range(4, 10), remainder.Range);
        Assert.All(result, r => Assert.True(r.Range.Duration > TimeSpan.Zero));
    }

    // Short remnants: a narrow gap between two exclusions is still
    // mathematically correct and must be preserved (not silently dropped)
    // - deciding whether it is "too short to use" is ClipCandidateGenerator's
    // job, not IntervalSubtractor's.
    [Fact]
    public void Subtract_NarrowGapBetweenTwoExclusions_IsPreservedAsShortRemainder()
    {
        var result = IntervalSubtractor.Subtract([Range(0, 10)], [Range(0, 4), Range(4.2, 10)]);

        var remainder = Assert.Single(result);
        Assert.Equal(TimeSpan.FromSeconds(4), remainder.Range.Start);
        Assert.Equal(TimeSpan.FromSeconds(4.2), remainder.Range.End);
        Assert.Equal(TimeSpan.FromMilliseconds(200), remainder.Range.Duration);
    }

    // Overlapping exclusions: two exclusions that overlap each other must
    // merge into one contiguous exclusion rather than each independently
    // (and incorrectly) carving out the scene range.
    [Fact]
    public void Subtract_OverlappingExclusions_MergeBeforeSubtracting()
    {
        var result = IntervalSubtractor.Subtract([Range(0, 10)], [Range(3, 6), Range(5, 8)]);

        Assert.Equal(2, result.Count);
        Assert.Equal(Range(0, 3), result[0].Range);
        Assert.Equal(Range(8, 10), result[1].Range);
    }

    [Fact]
    public void Subtract_DuplicateExclusions_MergeToOne()
    {
        var result = IntervalSubtractor.Subtract([Range(0, 10)], [Range(4, 6), Range(4, 6)]);

        Assert.Equal(2, result.Count);
        Assert.Equal(Range(0, 4), result[0].Range);
        Assert.Equal(Range(6, 10), result[1].Range);
    }

    [Fact]
    public void Subtract_UnsortedExclusions_MergedCorrectlyRegardlessOfInputOrder()
    {
        var result = IntervalSubtractor.Subtract([Range(0, 20)], [Range(15, 18), Range(2, 5), Range(4, 6)]);

        Assert.Equal(3, result.Count);
        Assert.Equal(Range(0, 2), result[0].Range);
        Assert.Equal(Range(6, 15), result[1].Range);
        Assert.Equal(Range(18, 20), result[2].Range);
    }

    [Fact]
    public void Subtract_MultipleSceneRanges_PreservesSourceSceneIndexPerRemainder()
    {
        var result = IntervalSubtractor.Subtract([Range(0, 5), Range(10, 15)], [Range(2, 3)]);

        Assert.Equal(3, result.Count);
        Assert.Equal(0, result[0].SourceSceneIndex);
        Assert.Equal(Range(0, 2), result[0].Range);
        Assert.Equal(0, result[1].SourceSceneIndex);
        Assert.Equal(Range(3, 5), result[1].Range);
        Assert.Equal(1, result[2].SourceSceneIndex);
        Assert.Equal(Range(10, 15), result[2].Range);
    }

    [Fact]
    public void Subtract_NeverAttemptsToRecoverAnExcludedInterval_EveryRemainderIsDisjointFromEveryExclusion()
    {
        var exclusions = new[] { Range(2, 3), Range(7, 7.5), Range(9, 20) };
        var result = IntervalSubtractor.Subtract([Range(0, 10)], exclusions);

        foreach (var remainder in result)
        {
            foreach (var exclusion in exclusions)
            {
                Assert.False(remainder.Range.Overlaps(exclusion));
            }
        }
    }
}
