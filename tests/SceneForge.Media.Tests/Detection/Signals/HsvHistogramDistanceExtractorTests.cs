using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Signals;

public class HsvHistogramDistanceExtractorTests
{
    private readonly HsvHistogramDistanceExtractor _extractor = new();

    [Fact]
    public void Extract_IdenticalFrames_IsNearZero()
    {
        using var frameA = FrameSampleBuilder.SolidColor(100, 150, 200);
        using var frameB = FrameSampleBuilder.SolidColor(100, 150, 200);
        using var analyzedA = AnalyzedFrame.Create(frameA);
        using var analyzedB = AnalyzedFrame.Create(frameB);

        var distance = _extractor.Extract(analyzedA, analyzedB);

        Assert.InRange(distance, 0.0, 0.01);
    }

    [Fact]
    public void Extract_OppositeColors_IsSubstantiallyGreaterThanIdentical()
    {
        using var black = FrameSampleBuilder.SolidColor(0, 0, 0);
        using var red = FrameSampleBuilder.SolidColor(0, 0, 255);
        using var analyzedBlack = AnalyzedFrame.Create(black);
        using var analyzedRed = AnalyzedFrame.Create(red);

        var identicalDistance = _extractor.Extract(analyzedBlack, analyzedBlack);
        var differentDistance = _extractor.Extract(analyzedBlack, analyzedRed);

        Assert.True(differentDistance > identicalDistance);
        Assert.InRange(differentDistance, 0.0, 1.0);
    }
}
