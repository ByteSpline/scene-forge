using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Signals;

public class AnalyzedFrameTests
{
    [Fact]
    public void Create_SolidBlackFrame_HasNearMaximumBlackScoreAndZeroLuminance()
    {
        using var frame = FrameSampleBuilder.SolidColor(0, 0, 0);
        using var analyzed = AnalyzedFrame.Create(frame);

        Assert.True(analyzed.BlackScore > 0.99);
        Assert.Equal(0.0, analyzed.WhiteScore);
        Assert.True(analyzed.MeanLuminance < 0.01);
    }

    [Fact]
    public void Create_SolidWhiteFrame_HasNearMaximumWhiteScoreAndFullLuminance()
    {
        using var frame = FrameSampleBuilder.SolidColor(255, 255, 255);
        using var analyzed = AnalyzedFrame.Create(frame);

        Assert.True(analyzed.WhiteScore > 0.99);
        Assert.Equal(0.0, analyzed.BlackScore);
        Assert.True(analyzed.MeanLuminance > 0.99);
    }

    [Fact]
    public void Create_SolidMidGrayFrame_HasZeroBlackAndWhiteScores()
    {
        using var frame = FrameSampleBuilder.SolidColor(128, 128, 128);
        using var analyzed = AnalyzedFrame.Create(frame);

        Assert.Equal(0.0, analyzed.BlackScore);
        Assert.Equal(0.0, analyzed.WhiteScore);
        Assert.InRange(analyzed.MeanLuminance, 0.45, 0.55);
    }

    [Fact]
    public void Create_SolidColorFrame_HasZeroEdgeDensityAndLaplacianVariance()
    {
        using var frame = FrameSampleBuilder.SolidColor(60, 60, 60);
        using var analyzed = AnalyzedFrame.Create(frame);

        Assert.Equal(0.0, analyzed.EdgeDensity);
        Assert.Equal(0.0, analyzed.LaplacianVariance);
    }

    [Fact]
    public void Create_CheckerboardFrame_HasPositiveEdgeDensityAndLaplacianVariance()
    {
        using var frame = FrameSampleBuilder.Checkerboard();
        using var analyzed = AnalyzedFrame.Create(frame);

        Assert.True(analyzed.EdgeDensity > 0);
        Assert.True(analyzed.LaplacianVariance > 0);
    }

    [Fact]
    public void Create_Gray8Frame_Throws()
    {
        using var frame = FrameSampleBuilder.Gray8SolidColor(128);

        Assert.Throws<TransitionDetectionException>(() => AnalyzedFrame.Create(frame));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        using var frame = FrameSampleBuilder.SolidColor(10, 20, 30);
        var analyzed = AnalyzedFrame.Create(frame);

        analyzed.Dispose();
        var exception = Record.Exception(() => analyzed.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Create_HsvHistogram_IsNormalizedToSumApproximatelyOne()
    {
        using var frame = FrameSampleBuilder.Checkerboard();
        using var analyzed = AnalyzedFrame.Create(frame);

        var sum = Cv2Sum(analyzed.HsvHistogram);

        Assert.InRange(sum, 0.98, 1.02);
    }

    private static double Cv2Sum(OpenCvSharp.Mat mat)
    {
        return OpenCvSharp.Cv2.Sum(mat).Val0;
    }
}
