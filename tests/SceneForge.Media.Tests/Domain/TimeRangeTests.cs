using SceneForge.Media.Domain;

namespace SceneForge.Media.Tests.Domain;

public class TimeRangeTests
{
    [Fact]
    public void Constructor_ValidRange_ComputesDuration()
    {
        var range = new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));

        Assert.Equal(TimeSpan.FromSeconds(3), range.Duration);
    }

    [Fact]
    public void Constructor_NegativeStart_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Constructor_EndBeforeStart_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Constructor_EqualStartAndEnd_ProducesZeroDuration()
    {
        var moment = TimeSpan.FromSeconds(5);

        var range = new TimeRange(moment, moment);

        Assert.Equal(TimeSpan.Zero, range.Duration);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(-1, false)]
    public void Contains_BoundaryAndOutOfRangeTimestamps(int seconds, bool expected)
    {
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(4));

        Assert.Equal(expected, range.Contains(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Overlaps_OverlappingRanges_ReturnsTrue()
    {
        var a = new TimeRange(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5));
        var b = new TimeRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8));

        Assert.True(a.Overlaps(b));
        Assert.True(b.Overlaps(a));
    }

    [Fact]
    public void Overlaps_AdjacentRanges_ReturnsFalse()
    {
        var a = new TimeRange(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5));
        var b = new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8));

        Assert.False(a.Overlaps(b));
        Assert.False(b.Overlaps(a));
    }

    [Fact]
    public void Overlaps_DisjointRanges_ReturnsFalse()
    {
        var a = new TimeRange(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2));
        var b = new TimeRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4));

        Assert.False(a.Overlaps(b));
    }
}
