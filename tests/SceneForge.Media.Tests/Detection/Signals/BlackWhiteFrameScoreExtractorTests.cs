using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Signals;

public class BlackWhiteFrameScoreExtractorTests
{
    private readonly BlackScoreExtractor _blackScoreExtractor = new();
    private readonly WhiteScoreExtractor _whiteScoreExtractor = new();

    [Fact]
    public void BlackScoreExtractor_BlackFrame_IsNearOne()
    {
        using var frame = FrameSampleBuilder.SolidColor(0, 0, 0);
        using var analyzed = AnalyzedFrame.Create(frame);

        Assert.True(_blackScoreExtractor.Extract(analyzed) > 0.99);
    }

    [Fact]
    public void WhiteScoreExtractor_BlackFrame_IsZero()
    {
        using var frame = FrameSampleBuilder.SolidColor(0, 0, 0);
        using var analyzed = AnalyzedFrame.Create(frame);

        Assert.Equal(0.0, _whiteScoreExtractor.Extract(analyzed));
    }

    [Fact]
    public void WhiteScoreExtractor_WhiteFrame_IsNearOne()
    {
        using var frame = FrameSampleBuilder.SolidColor(255, 255, 255);
        using var analyzed = AnalyzedFrame.Create(frame);

        Assert.True(_whiteScoreExtractor.Extract(analyzed) > 0.99);
    }

    [Fact]
    public void BlackScoreExtractor_MidGrayFrame_IsZero()
    {
        using var frame = FrameSampleBuilder.SolidColor(128, 128, 128);
        using var analyzed = AnalyzedFrame.Create(frame);

        Assert.Equal(0.0, _blackScoreExtractor.Extract(analyzed));
    }
}
