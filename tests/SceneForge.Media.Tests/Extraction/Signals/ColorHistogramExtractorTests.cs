using OpenCvSharp;
using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Extraction.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Extraction.Signals;

public class ColorHistogramExtractorTests
{
    [Fact]
    public void Extract_ReturnsOneBinPerHueRowOfTheUnderlyingHsvHistogram()
    {
        using var frame = FrameSampleBuilder.Checkerboard();
        using var analyzed = AnalyzedFrame.Create(frame);

        var histogram = ColorHistogramExtractor.Extract(analyzed);

        Assert.Equal(analyzed.HsvHistogram.Rows, histogram.Length);
    }

    [Fact]
    public void Extract_SumsToApproximatelyOne()
    {
        using var frame = FrameSampleBuilder.Checkerboard();
        using var analyzed = AnalyzedFrame.Create(frame);

        var histogram = ColorHistogramExtractor.Extract(analyzed);

        Assert.InRange(histogram.Sum(), 0.98, 1.02);
    }

    [Fact]
    public void Extract_DifferentHueFrames_ProduceDifferentHistograms()
    {
        using var red = FrameSampleBuilder.SolidColor(0, 0, 255);
        using var blue = FrameSampleBuilder.SolidColor(255, 0, 0);
        using var analyzedRed = AnalyzedFrame.Create(red);
        using var analyzedBlue = AnalyzedFrame.Create(blue);

        var histogramRed = ColorHistogramExtractor.Extract(analyzedRed);
        var histogramBlue = ColorHistogramExtractor.Extract(analyzedBlue);

        Assert.NotEqual(histogramRed, histogramBlue);
    }

    [Fact]
    public void Extract_AllValuesNonNegative()
    {
        using var frame = FrameSampleBuilder.Checkerboard();
        using var analyzed = AnalyzedFrame.Create(frame);

        var histogram = ColorHistogramExtractor.Extract(analyzed);

        Assert.All(histogram, value => Assert.True(value >= 0));
    }
}
