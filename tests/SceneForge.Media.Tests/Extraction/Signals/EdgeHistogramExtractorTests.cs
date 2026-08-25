using OpenCvSharp;
using SceneForge.Media.Extraction.Signals;

namespace SceneForge.Media.Tests.Extraction.Signals;

public class EdgeHistogramExtractorTests
{
    [Fact]
    public void Extract_SolidColorFrame_EveryCellIsZero()
    {
        using var gray = new Mat(64, 64, MatType.CV_8UC1, new Scalar(128));

        var histogram = EdgeHistogramExtractor.Extract(gray);

        Assert.Equal(EdgeHistogramExtractor.GridSize * EdgeHistogramExtractor.GridSize, histogram.Length);
        Assert.All(histogram, value => Assert.Equal(0.0f, value));
    }

    [Fact]
    public void Extract_CheckerboardFrame_EveryCellIsPositive()
    {
        using var gray = Checkerboard(64, 64, squareSize: 4);

        var histogram = EdgeHistogramExtractor.Extract(gray);

        Assert.All(histogram, value => Assert.True(value > 0));
    }

    [Fact]
    public void Extract_AllValuesWithinZeroToOne()
    {
        using var gray = Checkerboard(64, 64, squareSize: 4);

        var histogram = EdgeHistogramExtractor.Extract(gray);

        Assert.All(histogram, value => Assert.InRange(value, 0.0f, 1.0f));
    }

    [Fact]
    public void ExtractFromEdges_GivenTheSameCannyOutputExtractWouldComputeInternally_ProducesIdenticalHistogram()
    {
        // AnalyzedFrame.Create already runs Canny once per frame for
        // whole-frame EdgeDensity; ExtractFromEdges lets Extraction reuse
        // that same edges Mat instead of recomputing Canny a second time.
        // This proves the reuse is value-preserving, not just faster: given
        // the exact edges Mat Extract(gray) would have computed internally,
        // ExtractFromEdges must produce a bit-identical grid histogram.
        using var gray = Checkerboard(64, 64, squareSize: 4);
        using var edges = new Mat();
        Cv2.Canny(gray, edges, 50, 150);

        var fromGray = EdgeHistogramExtractor.Extract(gray);
        var fromEdges = EdgeHistogramExtractor.ExtractFromEdges(edges);

        Assert.Equal(fromGray, fromEdges);
    }

    [Fact]
    public void BorderDensity_ExceedsInteriorDensity_WhenEdgesAreConfinedToTheBorderRegion()
    {
        using var gray = BorderOnlyCheckerboard(64, cellSize: 16);

        var histogram = EdgeHistogramExtractor.Extract(gray);
        var border = EdgeHistogramExtractor.BorderDensity(histogram);
        var interior = EdgeHistogramExtractor.InteriorDensity(histogram);

        // Interior stays near zero: the only edge pixels that bleed into it
        // come from the boundary between the checkered border ring and the
        // flat interior fill, not from genuine interior detail.
        Assert.True(interior < 0.05);
        Assert.True(border > interior);
    }

    [Fact]
    public void InteriorDensity_ExceedsBorderDensity_WhenEdgesAreConfinedToTheInteriorRegion()
    {
        using var gray = InteriorOnlyCheckerboard(64, cellSize: 16);

        var histogram = EdgeHistogramExtractor.Extract(gray);
        var border = EdgeHistogramExtractor.BorderDensity(histogram);
        var interior = EdgeHistogramExtractor.InteriorDensity(histogram);

        Assert.True(border < 0.05);
        Assert.True(interior > border);
    }

    private static Mat Checkerboard(int width, int height, int squareSize)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var isLight = ((x / squareSize) + (y / squareSize)) % 2 == 0;
                mat.Set(y, x, isLight ? (byte)255 : (byte)0);
            }
        }

        return mat;
    }

    private static Mat BorderOnlyCheckerboard(int size, int cellSize)
    {
        var mat = new Mat(size, size, MatType.CV_8UC1, new Scalar(128));
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var inBorder = x < cellSize || x >= size - cellSize || y < cellSize || y >= size - cellSize;
                if (!inBorder)
                {
                    continue;
                }

                var isLight = ((x / 4) + (y / 4)) % 2 == 0;
                mat.Set(y, x, isLight ? (byte)255 : (byte)0);
            }
        }

        return mat;
    }

    private static Mat InteriorOnlyCheckerboard(int size, int cellSize)
    {
        var mat = new Mat(size, size, MatType.CV_8UC1, new Scalar(128));
        for (var y = cellSize; y < size - cellSize; y++)
        {
            for (var x = cellSize; x < size - cellSize; x++)
            {
                var isLight = ((x / 4) + (y / 4)) % 2 == 0;
                mat.Set(y, x, isLight ? (byte)255 : (byte)0);
            }
        }

        return mat;
    }
}
