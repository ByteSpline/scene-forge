using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Signals;

public class StructuralDifferenceExtractorTests
{
    private readonly StructuralDifferenceExtractor _extractor = new();

    [Fact]
    public void Extract_IdenticalFrames_IsZero()
    {
        using var frameA = FrameSampleBuilder.Checkerboard();
        using var frameB = FrameSampleBuilder.Checkerboard();
        using var analyzedA = AnalyzedFrame.Create(frameA);
        using var analyzedB = AnalyzedFrame.Create(frameB);

        Assert.Equal(0.0, _extractor.Extract(analyzedA, analyzedB));
    }

    [Fact]
    public void Extract_BlackToWhite_IsOne()
    {
        using var black = FrameSampleBuilder.SolidColor(0, 0, 0);
        using var white = FrameSampleBuilder.SolidColor(255, 255, 255);
        using var analyzedBlack = AnalyzedFrame.Create(black);
        using var analyzedWhite = AnalyzedFrame.Create(white);

        var difference = _extractor.Extract(analyzedBlack, analyzedWhite);

        Assert.Equal(1.0, difference, precision: 3);
    }
}
