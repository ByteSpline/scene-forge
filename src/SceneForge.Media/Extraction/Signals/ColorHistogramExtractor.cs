using OpenCvSharp;
using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Extraction.Signals;

// Projects AnalyzedFrame's already-computed, L1-normalized 2D Hue x
// Saturation histogram down to a 1D hue-only histogram by summing across
// the saturation axis - reuses Detection.Signals' existing HSV histogram
// computation rather than converting to HSV and calling Cv2.CalcHist a
// second time for the same frame.
internal static class ColorHistogramExtractor
{
    public static float[] Extract(AnalyzedFrame frame)
    {
        using var reduced = new Mat();
        Cv2.Reduce(frame.HsvHistogram, reduced, dim: ReduceDimension.Column, rtype: ReduceTypes.Sum, dtype: MatType.CV_32F);

        var hueBins = reduced.Rows;
        var histogram = new float[hueBins];
        for (var i = 0; i < hueBins; i++)
        {
            histogram[i] = reduced.At<float>(i, 0);
        }

        return histogram;
    }
}
