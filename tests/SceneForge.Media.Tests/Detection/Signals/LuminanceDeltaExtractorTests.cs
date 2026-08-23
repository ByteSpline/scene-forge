using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Signals;

public class LuminanceDeltaExtractorTests
{
    private readonly LuminanceDeltaExtractor _extractor = new();

    [Fact]
    public void Extract_DarkToLight_IsPositive()
    {
        using var dark = FrameSampleBuilder.SolidColor(0, 0, 0);
        using var light = FrameSampleBuilder.SolidColor(255, 255, 255);
        using var analyzedDark = AnalyzedFrame.Create(dark);
        using var analyzedLight = AnalyzedFrame.Create(light);

        var delta = _extractor.Extract(analyzedDark, analyzedLight);

        Assert.True(delta > 0.9);
    }

    [Fact]
    public void Extract_LightToDark_IsNegative()
    {
        using var dark = FrameSampleBuilder.SolidColor(0, 0, 0);
        using var light = FrameSampleBuilder.SolidColor(255, 255, 255);
        using var analyzedDark = AnalyzedFrame.Create(dark);
        using var analyzedLight = AnalyzedFrame.Create(light);

        var delta = _extractor.Extract(analyzedLight, analyzedDark);

        Assert.True(delta < -0.9);
    }

    [Fact]
    public void Extract_IdenticalFrames_IsZero()
    {
        using var frameA = FrameSampleBuilder.SolidColor(80, 80, 80);
        using var frameB = FrameSampleBuilder.SolidColor(80, 80, 80);
        using var analyzedA = AnalyzedFrame.Create(frameA);
        using var analyzedB = AnalyzedFrame.Create(frameB);

        Assert.Equal(0.0, _extractor.Extract(analyzedA, analyzedB));
    }
}
