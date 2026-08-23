using OpenCvSharp;

namespace SceneForge.Media.Extraction.Signals;

// Divides a grayscale frame into a GridSize x GridSize grid and computes
// each cell's own Canny edge-pixel fraction independently (each cell value
// is its own 0..1 density - this is NOT normalized to sum to 1 across
// cells, unlike ColorHistogramExtractor's output, since a mostly-blank
// frame and a heavily-detailed frame are meaningfully different states that
// summing-to-1 would erase). Doubles as both the EdgeHistogram descriptor
// and, via the outer-ring/inner-cells split below, the source for
// ClipScore.OverlaySuspicion's border-vs-interior comparison - one Canny
// pass serves both purposes rather than computing edges twice.
internal static class EdgeHistogramExtractor
{
    private const double CannyLowThreshold = 50;
    private const double CannyHighThreshold = 150;
    internal const int GridSize = 4;

    public static float[] Extract(Mat gray)
    {
        using var edges = new Mat();
        Cv2.Canny(gray, edges, CannyLowThreshold, CannyHighThreshold);

        var cellWidth = edges.Cols / GridSize;
        var cellHeight = edges.Rows / GridSize;
        var histogram = new float[GridSize * GridSize];
        var index = 0;

        for (var row = 0; row < GridSize; row++)
        {
            var isLastRow = row == GridSize - 1;
            var height = isLastRow ? edges.Rows - (row * cellHeight) : cellHeight;

            for (var col = 0; col < GridSize; col++)
            {
                var isLastCol = col == GridSize - 1;
                var width = isLastCol ? edges.Cols - (col * cellWidth) : cellWidth;

                using var cell = new Mat(edges, new Rect(col * cellWidth, row * cellHeight, width, height));
                histogram[index] = (float)(Cv2.CountNonZero(cell) / (double)(width * height));
                index++;
            }
        }

        return histogram;
    }

    // Mean density of the outer-ring cells (row/col 0 or GridSize-1) -
    // caption/lower-third/watermark-prone regions.
    public static double BorderDensity(IReadOnlyList<float> edgeHistogram)
    {
        double sum = 0;
        var count = 0;
        for (var row = 0; row < GridSize; row++)
        {
            for (var col = 0; col < GridSize; col++)
            {
                if (row == 0 || row == GridSize - 1 || col == 0 || col == GridSize - 1)
                {
                    sum += edgeHistogram[(row * GridSize) + col];
                    count++;
                }
            }
        }

        return count == 0 ? 0.0 : sum / count;
    }

    // Mean density of the remaining inner cells.
    public static double InteriorDensity(IReadOnlyList<float> edgeHistogram)
    {
        double sum = 0;
        var count = 0;
        for (var row = 1; row < GridSize - 1; row++)
        {
            for (var col = 1; col < GridSize - 1; col++)
            {
                sum += edgeHistogram[(row * GridSize) + col];
                count++;
            }
        }

        return count == 0 ? 0.0 : sum / count;
    }
}
