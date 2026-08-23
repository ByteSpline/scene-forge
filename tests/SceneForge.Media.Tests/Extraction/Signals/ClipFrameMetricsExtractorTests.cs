using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Extraction.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Extraction.Signals;

public class ClipFrameMetricsExtractorTests
{
    [Fact]
    public void Build_NoPreviousFrame_HasZeroStructuralDifference()
    {
        using var frame = FrameSampleBuilder.Checkerboard(timestamp: TimeSpan.FromSeconds(1));
        using var analyzed = AnalyzedFrame.Create(frame);

        var metrics = ClipFrameMetricsExtractor.Build(null, analyzed);

        Assert.Equal(0.0, metrics.StructuralDifferenceFromPrevious);
        Assert.Equal(TimeSpan.FromSeconds(1), metrics.Timestamp);
    }

    [Fact]
    public void Build_TwoDifferentFrames_HasPositiveStructuralDifference()
    {
        using var black = FrameSampleBuilder.SolidColor(0, 0, 0);
        using var white = FrameSampleBuilder.SolidColor(255, 255, 255);
        using var analyzedBlack = AnalyzedFrame.Create(black);
        using var analyzedWhite = AnalyzedFrame.Create(white);

        var metrics = ClipFrameMetricsExtractor.Build(analyzedBlack, analyzedWhite);

        Assert.True(metrics.StructuralDifferenceFromPrevious > 0.9);
    }

    [Fact]
    public void Build_TwoIdenticalFrames_HasZeroStructuralDifference()
    {
        using var frameA = FrameSampleBuilder.SolidColor(50, 50, 50);
        using var frameB = FrameSampleBuilder.SolidColor(50, 50, 50);
        using var analyzedA = AnalyzedFrame.Create(frameA);
        using var analyzedB = AnalyzedFrame.Create(frameB);

        var metrics = ClipFrameMetricsExtractor.Build(analyzedA, analyzedB);

        Assert.Equal(0.0, metrics.StructuralDifferenceFromPrevious);
    }

    [Fact]
    public void Build_SharpnessMatchesUnderlyingAnalyzedFrameLaplacianVariance()
    {
        using var frame = FrameSampleBuilder.Checkerboard();
        using var analyzed = AnalyzedFrame.Create(frame);

        var metrics = ClipFrameMetricsExtractor.Build(null, analyzed);

        Assert.Equal(analyzed.LaplacianVariance, metrics.Sharpness);
    }

    [Fact]
    public void Build_PopulatesPerceptualHashAndHistograms()
    {
        using var frame = FrameSampleBuilder.Checkerboard();
        using var analyzed = AnalyzedFrame.Create(frame);

        var metrics = ClipFrameMetricsExtractor.Build(null, analyzed);

        Assert.NotEmpty(metrics.ColorHistogram);
        Assert.Equal(16, metrics.EdgeHistogram.Count);
    }

    [Fact]
    public void Build_SolidBlackFrame_HasNearMaximumBlackScore()
    {
        using var frame = FrameSampleBuilder.SolidColor(0, 0, 0);
        using var analyzed = AnalyzedFrame.Create(frame);

        var metrics = ClipFrameMetricsExtractor.Build(null, analyzed);

        Assert.True(metrics.BlackScore > 0.99);
        Assert.Equal(0.0, metrics.WhiteScore);
    }
}
