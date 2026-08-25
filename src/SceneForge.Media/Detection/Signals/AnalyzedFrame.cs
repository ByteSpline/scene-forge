using System.Runtime.InteropServices;
using OpenCvSharp;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Detection.Signals;

// Wraps exactly one FrameSample's pixel data as OpenCvSharp Mats plus a
// handful of eagerly-computed per-frame scalar signals, decoupled entirely
// from the FrameSample's own lifetime: pixel bytes are copied into
// independently-owned native Mat memory at construction time (never a view
// over FrameSample.Buffer), so the source FrameSample can be disposed
// (returning its ArrayPool buffer) immediately after Create returns without
// risking any use-after-free/reuse corruption of these Mats. Only Bgr24
// frames are supported - Detection needs color for the HSV signal, unlike
// Sampling which allows Gray8 for cheaper decode when color isn't needed.
//
// Lifetime: at most two instances are ever alive at once in the detection
// pipeline (the previous and current frame of a pair) - see SignalPipeline.
// Dispose releases all native Mat memory; failing to dispose leaks native
// (not managed-heap) memory, so callers must always dispose promptly.
internal sealed class AnalyzedFrame : IDisposable
{
    private const int HueBins = 32;
    private const int SaturationBins = 32;
    private const byte BlackPixelMaxValue = 16;
    private const byte WhitePixelMinValue = 240;
    private const double CannyLowThreshold = 50;
    private const double CannyHighThreshold = 150;

    private bool _disposed;

    private AnalyzedFrame(
        TimeSpan timestamp,
        Mat gray,
        Mat hsvHistogram,
        Mat edges,
        double meanLuminance,
        double edgeDensity,
        double laplacianVariance,
        double blackScore,
        double whiteScore)
    {
        Timestamp = timestamp;
        Gray = gray;
        HsvHistogram = hsvHistogram;
        Edges = edges;
        MeanLuminance = meanLuminance;
        EdgeDensity = edgeDensity;
        LaplacianVariance = laplacianVariance;
        BlackScore = blackScore;
        WhiteScore = whiteScore;
    }

    public TimeSpan Timestamp { get; }

    // Single-channel 8-bit grayscale. Owned by this instance.
    public Mat Gray { get; }

    // Normalized (L1) 2D histogram over Hue x Saturation, HueBins x
    // SaturationBins, CV_32F. Owned by this instance.
    public Mat HsvHistogram { get; }

    // Single-channel 8-bit Canny edge map (255 at an edge pixel, 0
    // elsewhere), same pass that produces EdgeDensity below. Retained
    // (rather than a Create-local `using` temporary) so
    // Extraction.Signals.EdgeHistogramExtractor.ExtractFromEdges can reuse
    // it instead of recomputing Canny a second time per frame. Owned by
    // this instance.
    public Mat Edges { get; }

    // Mean grayscale intensity, normalized to 0..1.
    public double MeanLuminance { get; }

    // Fraction of pixels classified as edges by Canny, 0..1.
    public double EdgeDensity { get; }

    // Variance of the Laplacian (focus measure) - higher means sharper.
    public double LaplacianVariance { get; }

    // Fraction of pixels at or below BlackPixelMaxValue, 0..1.
    public double BlackScore { get; }

    // Fraction of pixels at or above WhitePixelMinValue, 0..1.
    public double WhiteScore { get; }

    public static AnalyzedFrame Create(FrameSample frame)
    {
        using var scratchBgr = new Mat();
        return Create(frame, scratchBgr);
    }

    // Same as Create(FrameSample), but writes into a caller-owned,
    // caller-reused BGR working Mat instead of allocating (and freeing) a
    // fresh one every call. All frames within one pipeline run share the
    // same fixed dimensions, so SignalPipeline/ClipFrameMetricsPipeline
    // each own exactly one scratchBgr for their whole streamed run - one
    // native buffer instead of one per frame. Resized in place (via
    // Mat.Create, a no-op if already the right size/type) only if it
    // doesn't already match this frame; the returned AnalyzedFrame's
    // Gray/HsvHistogram/Edges are always independently-allocated
    // destination Mats, never views over scratchBgr, so overwriting
    // scratchBgr on the next call can never corrupt an already-returned
    // AnalyzedFrame.
    public static AnalyzedFrame Create(FrameSample frame, Mat scratchBgr)
    {
        ArgumentNullException.ThrowIfNull(scratchBgr);
        if (frame.PixelFormat != FrameSamplePixelFormat.Bgr24)
        {
            throw new TransitionDetectionException(
                $"AnalyzedFrame requires {nameof(FrameSamplePixelFormat.Bgr24)} frames; got {frame.PixelFormat}.");
        }

        if (scratchBgr.Empty() || scratchBgr.Rows != frame.Height || scratchBgr.Cols != frame.Width || scratchBgr.Type() != MatType.CV_8UC3)
        {
            scratchBgr.Create(frame.Height, frame.Width, MatType.CV_8UC3);
        }

        Marshal.Copy(frame.Buffer, 0, scratchBgr.Data, frame.ByteLength);
        var bgr = scratchBgr;

        var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

        var histogram = new Mat();
        Cv2.CalcHist(
            [hsv],
            [0, 1],
            null,
            histogram,
            2,
            [HueBins, SaturationBins],
            [new Rangef(0, 180), new Rangef(0, 256)]);
        Cv2.Normalize(histogram, histogram, 1.0, 0.0, NormTypes.L1);

        var meanLuminance = Cv2.Mean(gray).Val0 / 255.0;

        var edges = new Mat();
        Cv2.Canny(gray, edges, CannyLowThreshold, CannyHighThreshold);
        var edgeDensity = Cv2.CountNonZero(edges) / (double)(edges.Rows * edges.Cols);

        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);
        Cv2.MeanStdDev(laplacian, out _, out var laplacianStdDev);
        var laplacianVariance = laplacianStdDev.Val0 * laplacianStdDev.Val0;

        var totalPixels = (double)(gray.Rows * gray.Cols);

        using var blackMask = new Mat();
        Cv2.Threshold(gray, blackMask, BlackPixelMaxValue, 255, ThresholdTypes.BinaryInv);
        var blackScore = Cv2.CountNonZero(blackMask) / totalPixels;

        using var whiteMask = new Mat();
        Cv2.Threshold(gray, whiteMask, WhitePixelMinValue - 1, 255, ThresholdTypes.Binary);
        var whiteScore = Cv2.CountNonZero(whiteMask) / totalPixels;

        return new AnalyzedFrame(
            frame.Timestamp,
            gray,
            histogram,
            edges,
            meanLuminance,
            edgeDensity,
            laplacianVariance,
            blackScore,
            whiteScore);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Gray.Dispose();
        HsvHistogram.Dispose();
        Edges.Dispose();
    }
}
