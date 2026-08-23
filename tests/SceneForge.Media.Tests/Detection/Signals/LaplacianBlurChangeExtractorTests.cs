using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Signals;

public class LaplacianBlurChangeExtractorTests
{
    private readonly LaplacianBlurChangeExtractor _extractor = new();

    [Fact]
    public void Extract_SharpToSolid_IsNegative()
    {
        using var sharp = FrameSampleBuilder.Checkerboard();
        using var solid = FrameSampleBuilder.SolidColor(60, 60, 60);
        using var analyzedSharp = AnalyzedFrame.Create(sharp);
        using var analyzedSolid = AnalyzedFrame.Create(solid);

        var delta = _extractor.Extract(analyzedSharp, analyzedSolid);

        Assert.True(delta < 0);
    }

    [Fact]
    public void Extract_SolidToSharp_IsPositive()
    {
        using var sharp = FrameSampleBuilder.Checkerboard();
        using var solid = FrameSampleBuilder.SolidColor(60, 60, 60);
        using var analyzedSharp = AnalyzedFrame.Create(sharp);
        using var analyzedSolid = AnalyzedFrame.Create(solid);

        var delta = _extractor.Extract(analyzedSolid, analyzedSharp);

        Assert.True(delta > 0);
    }

    [Fact]
    public void Extract_IdenticalFrames_IsZero()
    {
        using var frameA = FrameSampleBuilder.Checkerboard();
        using var frameB = FrameSampleBuilder.Checkerboard();
        using var analyzedA = AnalyzedFrame.Create(frameA);
        using var analyzedB = AnalyzedFrame.Create(frameB);

        Assert.Equal(0.0, _extractor.Extract(analyzedA, analyzedB));
    }
}
