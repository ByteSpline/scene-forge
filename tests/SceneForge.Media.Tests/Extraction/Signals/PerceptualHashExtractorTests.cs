using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Extraction.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Extraction.Signals;

public class PerceptualHashExtractorTests
{
    [Fact]
    public void Extract_IdenticalFrames_ProducesIdenticalHash()
    {
        using var frameA = FrameSampleBuilder.Checkerboard();
        using var frameB = FrameSampleBuilder.Checkerboard();
        using var analyzedA = AnalyzedFrame.Create(frameA);
        using var analyzedB = AnalyzedFrame.Create(frameB);

        var hashA = PerceptualHashExtractor.Extract(analyzedA.Gray);
        var hashB = PerceptualHashExtractor.Extract(analyzedB.Gray);

        Assert.Equal(hashA, hashB);
        Assert.Equal(0, PerceptualHashExtractor.HammingDistance(hashA, hashB));
    }

    [Fact]
    public void Extract_VeryDifferentFrames_ProducesLargerHammingDistanceThanNearIdenticalFrames()
    {
        using var solidBlack = FrameSampleBuilder.SolidColor(0, 0, 0);
        using var solidBlackAgain = FrameSampleBuilder.SolidColor(1, 1, 1);
        using var checkerboard = FrameSampleBuilder.Checkerboard();

        using var analyzedBlack = AnalyzedFrame.Create(solidBlack);
        using var analyzedBlackAgain = AnalyzedFrame.Create(solidBlackAgain);
        using var analyzedCheckerboard = AnalyzedFrame.Create(checkerboard);

        var hashBlack = PerceptualHashExtractor.Extract(analyzedBlack.Gray);
        var hashBlackAgain = PerceptualHashExtractor.Extract(analyzedBlackAgain.Gray);
        var hashCheckerboard = PerceptualHashExtractor.Extract(analyzedCheckerboard.Gray);

        var nearIdenticalDistance = PerceptualHashExtractor.HammingDistance(hashBlack, hashBlackAgain);
        var veryDifferentDistance = PerceptualHashExtractor.HammingDistance(hashBlack, hashCheckerboard);

        Assert.True(veryDifferentDistance > nearIdenticalDistance);
    }

    [Theory]
    [InlineData(0UL, 0UL, 0)]
    [InlineData(0UL, 0b1UL, 1)]
    [InlineData(0UL, 0b111UL, 3)]
    public void HammingDistance_CountsDifferingBits(ulong a, ulong b, int expected)
    {
        Assert.Equal(expected, PerceptualHashExtractor.HammingDistance(a, b));
    }

    [Fact]
    public void HammingDistance_AllBitsDiffer_Returns64()
    {
        Assert.Equal(64, PerceptualHashExtractor.HammingDistance(0UL, ulong.MaxValue));
    }
}
