using OpenCvSharp;

namespace SceneForge.Media.Detection.Signals;

// Bhattacharyya distance (0 = identical, 1 = maximally different for
// normalized histograms) between consecutive frames' Hue/Saturation
// histograms. Robust to small luminance shifts (unlike a raw pixel diff),
// which is what makes it useful for catching color-composition changes a
// dissolve/hard-cut causes even when overall brightness stays similar.
internal sealed class HsvHistogramDistanceExtractor : IPairSignalExtractor
{
    public string Name => nameof(HsvHistogramDistanceExtractor);

    public double Extract(AnalyzedFrame previous, AnalyzedFrame current) =>
        Cv2.CompareHist(previous.HsvHistogram, current.HsvHistogram, HistCompMethods.Bhattacharyya);
}
