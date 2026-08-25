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
    public void Create_CheckerboardFrame_EdgesMatMatchesEdgeDensity()
    {
        // EdgeDensity is derived from Edges (CountNonZero / totalPixels) -
        // the retained Edges Mat exists so Extraction can reuse this same
        // Canny pass instead of recomputing it; this proves the two stay
        // in lockstep rather than drifting apart from independent
        // computation.
        using var frame = FrameSampleBuilder.Checkerboard();
        using var analyzed = AnalyzedFrame.Create(frame);

        Assert.Equal(analyzed.Gray.Rows, analyzed.Edges.Rows);
        Assert.Equal(analyzed.Gray.Cols, analyzed.Edges.Cols);
        var expectedDensity = OpenCvSharp.Cv2.CountNonZero(analyzed.Edges) / (double)(analyzed.Edges.Rows * analyzed.Edges.Cols);
        Assert.Equal(expectedDensity, analyzed.EdgeDensity);
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
    public void Create_GivenReusableScratchBgrMat_ProducesSameResultAsWithoutOne()
    {
        using var frame = FrameSampleBuilder.Checkerboard();
        using var scratchBgr = new OpenCvSharp.Mat();

        using var withoutScratch = AnalyzedFrame.Create(frame);
        using var withScratch = AnalyzedFrame.Create(frame, scratchBgr);

        Assert.Equal(withoutScratch.MeanLuminance, withScratch.MeanLuminance);
        Assert.Equal(withoutScratch.EdgeDensity, withScratch.EdgeDensity);
        Assert.Equal(withoutScratch.LaplacianVariance, withScratch.LaplacianVariance);
        Assert.Equal(withoutScratch.BlackScore, withScratch.BlackScore);
        Assert.Equal(withoutScratch.WhiteScore, withScratch.WhiteScore);
    }

    [Fact]
    public void Create_ScratchBgrMatReusedAcrossTwoDifferentFrames_EachResultStaysIndependentlyCorrect()
    {
        // The whole point of the reusable scratch Mat is that its native
        // buffer gets overwritten by the next Create call - this proves
        // that overwrite never corrupts an already-returned AnalyzedFrame,
        // because Gray/HsvHistogram/Edges are always freshly-allocated
        // destination Mats, never views over the scratch buffer.
        using var frameA = FrameSampleBuilder.SolidColor(0, 0, 0);
        using var frameB = FrameSampleBuilder.SolidColor(255, 255, 255);
        using var scratchBgr = new OpenCvSharp.Mat();

        using var analyzedA = AnalyzedFrame.Create(frameA, scratchBgr);
        using var analyzedB = AnalyzedFrame.Create(frameB, scratchBgr);

        Assert.True(analyzedA.BlackScore > 0.99);
        Assert.True(analyzedA.MeanLuminance < 0.01);
        Assert.True(analyzedB.WhiteScore > 0.99);
        Assert.True(analyzedB.MeanLuminance > 0.99);
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
