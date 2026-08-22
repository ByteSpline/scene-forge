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
}
