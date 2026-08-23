using SceneForge.Media.Domain;

namespace SceneForge.Media.Tests.Domain;

public class RationalFrameRateTests
{
    [Fact]
    public void Parse_WellFormedRational_ReturnsDefinedValue()
    {
        var frameRate = RationalFrameRate.Parse("30000/1001");

        Assert.True(frameRate.IsDefined);
        Assert.Equal(30000, frameRate.Numerator);
        Assert.Equal(1001, frameRate.Denominator);
        Assert.Equal(30000.0 / 1001.0, frameRate.ToDouble());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0/0")]
    public void Parse_MissingOrZeroOverZero_ReturnsUndefined(string? value)
    {
        var frameRate = RationalFrameRate.Parse(value);

        Assert.False(frameRate.IsDefined);
        Assert.Null(frameRate.ToDouble());
        Assert.Equal(RationalFrameRate.Undefined, frameRate);
    }

    [Theory]
    [InlineData("not-a-rate")]
    [InlineData("25")]
    [InlineData("25/1/1")]
    [InlineData("a/b")]
    public void Parse_MalformedValue_Throws(string value)
    {
        Assert.Throws<FormatException>(() => RationalFrameRate.Parse(value));
    }

    [Fact]
    public void Constructor_NegativeDenominator_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RationalFrameRate(25, -1));
    }

    [Fact]
    public void Constructor_ZeroDenominatorWithNonZeroNumerator_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RationalFrameRate(25, 0));
    }

    [Fact]
    public void ToString_DefinedValue_UsesRationalForm()
    {
        var frameRate = new RationalFrameRate(25, 1);

        Assert.Equal("25/1", frameRate.ToString());
    }

    [Fact]
    public void ToString_Undefined_ReadsAsUndefined()
    {
        Assert.Equal("undefined", RationalFrameRate.Undefined.ToString());
    }

    [Fact]
    public void ToFrameCount_IntegerFrameRate_OneSecondIsExactlyFrameRateFrames()
    {
        var frameRate = new RationalFrameRate(25, 1);

        Assert.Equal(25, frameRate.ToFrameCount(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, frameRate.ToFrameCount(TimeSpan.Zero));
        Assert.Equal(250, frameRate.ToFrameCount(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void FromFrameCount_IntegerFrameRate_IsExactRoundTrip()
    {
        var frameRate = new RationalFrameRate(25, 1);

        Assert.Equal(TimeSpan.FromSeconds(1), frameRate.FromFrameCount(25));
        Assert.Equal(TimeSpan.Zero, frameRate.FromFrameCount(0));
    }

    [Fact]
    public void ToFrameCount_RoundsToNearestFrame()
    {
        var frameRate = new RationalFrameRate(30000, 1001);

        // 1 frame at 29.97fps is ~33.3667ms; 40ms rounds up to the 2nd frame boundary.
        Assert.Equal(1, frameRate.ToFrameCount(TimeSpan.FromMilliseconds(40)));

        // 10ms rounds down to the 0th frame boundary.
        Assert.Equal(0, frameRate.ToFrameCount(TimeSpan.FromMilliseconds(10)));
    }

    [Theory]
    [InlineData(25, 1, 0)]
    [InlineData(25, 1, 1)]
    [InlineData(25, 1, 1000)]
    [InlineData(30000, 1001, 0)]
    [InlineData(30000, 1001, 12345)]
    [InlineData(24, 1, 7)]
    public void FromFrameCount_ThenToFrameCount_RoundTripsToSameFrameCount(long numerator, long denominator, long frameCount)
    {
        var frameRate = new RationalFrameRate(numerator, denominator);

        var duration = frameRate.FromFrameCount(frameCount);
        var roundTripped = frameRate.ToFrameCount(duration);

        Assert.Equal(frameCount, roundTripped);
    }

    [Fact]
    public void ToFrameCount_Undefined_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => RationalFrameRate.Undefined.ToFrameCount(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void FromFrameCount_Undefined_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => RationalFrameRate.Undefined.FromFrameCount(1));
    }

    [Fact]
    public void ToFrameCount_NegativeDuration_Throws()
    {
        var frameRate = new RationalFrameRate(25, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => frameRate.ToFrameCount(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void FromFrameCount_NegativeFrameCount_Throws()
    {
        var frameRate = new RationalFrameRate(25, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => frameRate.FromFrameCount(-1));
    }
}
