using SceneForge.Media.Detection.Classification;

namespace SceneForge.Media.Tests.Detection.Classification;

public class ContiguousRunFinderTests
{
    [Fact]
    public void FindRuns_NoMatches_ReturnsEmpty()
    {
        var runs = ContiguousRunFinder.FindRuns(5, _ => false, 1);

        Assert.Empty(runs);
    }

    [Fact]
    public void FindRuns_SingleRunMeetingMinLength_IsReturned()
    {
        var runs = ContiguousRunFinder.FindRuns(5, i => i is >= 1 and <= 3, 2);

        var run = Assert.Single(runs);
        Assert.Equal((1, 3), run);
    }

    [Fact]
    public void FindRuns_RunShorterThanMinLength_IsExcluded()
    {
        var runs = ContiguousRunFinder.FindRuns(5, i => i == 2, 2);

        Assert.Empty(runs);
    }

    [Fact]
    public void FindRuns_MultipleDisjointRuns_AreAllReturned()
    {
        var runs = ContiguousRunFinder.FindRuns(10, i => i is (1 or 2 or 6 or 7 or 8), 2);

        Assert.Equal([(1, 2), (6, 8)], runs);
    }

    [Fact]
    public void FindRuns_RunExtendingToEnd_IsIncluded()
    {
        var runs = ContiguousRunFinder.FindRuns(5, i => i >= 3, 2);

        var run = Assert.Single(runs);
        Assert.Equal((3, 4), run);
    }
}
