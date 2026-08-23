using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Signals;

public class EdgeDensityDeltaExtractorTests
{
    private readonly EdgeDensityDeltaExtractor _extractor = new();

    [Fact]
    public void Extract_SolidToCheckerboard_IsPositive()
    {
        using var solid = FrameSampleBuilder.SolidColor(40, 40, 40);
        using var checkerboard = FrameSampleBuilder.Checkerboard();
        using var analyzedSolid = AnalyzedFrame.Create(solid);
        using var analyzedCheckerboard = AnalyzedFrame.Create(checkerboard);

        var delta = _extractor.Extract(analyzedSolid, analyzedCheckerboard);

        Assert.True(delta > 0);
    }

    [Fact]
    public void Extract_CheckerboardToSolid_IsNegative()
    {
        using var solid = FrameSampleBuilder.SolidColor(40, 40, 40);
        using var checkerboard = FrameSampleBuilder.Checkerboard();
        using var analyzedSolid = AnalyzedFrame.Create(solid);
        using var analyzedCheckerboard = AnalyzedFrame.Create(checkerboard);

        var delta = _extractor.Extract(analyzedCheckerboard, analyzedSolid);

        Assert.True(delta < 0);
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
