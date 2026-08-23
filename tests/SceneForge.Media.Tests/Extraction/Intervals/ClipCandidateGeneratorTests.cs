using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Extraction.Intervals;

namespace SceneForge.Media.Tests.Extraction.Intervals;

public class ClipCandidateGeneratorTests
{
    private static readonly CleanClipScoringOptions Options = new()
    {
        MinClipDuration = TimeSpan.FromSeconds(3),
        MaxClipDuration = TimeSpan.FromSeconds(5),
        BoundaryGuard = TimeSpan.FromSeconds(1),
        OverlapFraction = 0.5,
    };

    private static IndexedTimeRange Remaining(int sourceIndex, double startSeconds, double endSeconds) =>
        new(sourceIndex, new TimeRange(TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds)));

    [Fact]
    public void Generate_RangeExactlyMaxDurationAfterGuard_ProducesOneMaxDurationCandidate()
    {
        // 1s guard trimmed from both ends of [0,7] leaves exactly [1,6], a
        // 5s span - exactly MaxClipDuration, so one full-length candidate.
        var candidates = ClipCandidateGenerator.Generate([Remaining(0, 0, 7)], Options);

        var candidate = Assert.Single(candidates);
        Assert.Equal(TimeSpan.FromSeconds(1), candidate.Range.Start);
        Assert.Equal(TimeSpan.FromSeconds(6), candidate.Range.End);
        Assert.Equal(TimeSpan.FromSeconds(5), candidate.Range.Duration);
    }

    [Fact]
    public void Generate_GuardedRangeShorterThanMinClipDuration_ProducesNoCandidate()
    {
        // 1s guard on both ends of [0,4] leaves [1,3], a 2s span - below
        // MinClipDuration (3s).
        var candidates = ClipCandidateGenerator.Generate([Remaining(0, 0, 4)], Options);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Generate_RangeShorterThanTwiceTheGuard_ProducesNoCandidate()
    {
        var candidates = ClipCandidateGenerator.Generate([Remaining(0, 0, 1.5)], Options);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Generate_GuardedRangeBetweenMinAndMax_ProducesOneCandidateAtTheGuardedLength()
    {
        // 1s guard on both ends of [0,6] leaves [1,5], a 4s span - between
        // Min (3s) and Max (5s), so the candidate is exactly that length.
        var candidates = ClipCandidateGenerator.Generate([Remaining(0, 0, 6)], Options);

        var candidate = Assert.Single(candidates);
        Assert.Equal(TimeSpan.FromSeconds(4), candidate.Range.Duration);
    }

    [Fact]
    public void Generate_LongRemainingRange_SlidesOverlappingCandidatesByConfiguredOverlap()
    {
        // Guarded range is 21s; MaxClipDuration 5s, 50% overlap -> stride
        // 2.5s. Candidates: [1,6], [3.5,8.5], [6,11], ... every one exactly
        // 5s, deterministic given exact TimeSpan-tick arithmetic.
        var candidates = ClipCandidateGenerator.Generate([Remaining(0, 0, 23)], Options);

        Assert.True(candidates.Count > 1);
        Assert.All(candidates, c => Assert.Equal(TimeSpan.FromSeconds(5), c.Range.Duration));
        Assert.Equal(TimeSpan.FromSeconds(1), candidates[0].Range.Start);
        Assert.Equal(TimeSpan.FromSeconds(3.5), candidates[1].Range.Start);
    }

    [Fact]
    public void Generate_EveryCandidateStaysWithinTheGuardedBounds()
    {
        var remaining = Remaining(0, 0, 23);
        var candidates = ClipCandidateGenerator.Generate([remaining], Options);

        var guardedStart = remaining.Range.Start + Options.BoundaryGuard;
        var guardedEnd = remaining.Range.End - Options.BoundaryGuard;

        Assert.All(candidates, c =>
        {
            Assert.True(c.Range.Start >= guardedStart);
            Assert.True(c.Range.End <= guardedEnd);
        });
    }

    [Fact]
    public void Generate_ZeroBoundaryGuard_UsesTheFullRemainingRange()
    {
        var options = Options with { BoundaryGuard = TimeSpan.Zero };
        var candidates = ClipCandidateGenerator.Generate([Remaining(0, 0, 5)], options);

        var candidate = Assert.Single(candidates);
        Assert.Equal(TimeSpan.Zero, candidate.Range.Start);
        Assert.Equal(TimeSpan.FromSeconds(5), candidate.Range.End);
    }

    [Fact]
    public void Generate_PreservesSourceSceneIndexOnEveryCandidate()
    {
        var candidates = ClipCandidateGenerator.Generate([Remaining(7, 0, 7)], Options);

        Assert.All(candidates, c => Assert.Equal(7, c.SourceSceneIndex));
    }

    [Fact]
    public void Generate_MultipleRemainingRanges_ReturnsCandidatesSortedByStart()
    {
        var candidates = ClipCandidateGenerator.Generate(
            [Remaining(1, 20, 26), Remaining(0, 0, 6)],
            Options);

        Assert.Equal(2, candidates.Count);
        Assert.True(candidates[0].Range.Start < candidates[1].Range.Start);
    }

    [Fact]
    public void Generate_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(ClipCandidateGenerator.Generate([], Options));
    }
}
