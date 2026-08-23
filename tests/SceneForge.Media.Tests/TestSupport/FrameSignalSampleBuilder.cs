using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Tests.TestSupport;

// Hand-builds FrameSignalSample/GlobalMotionEstimate sequences with known
// values, for deterministic classifier unit tests with no OpenCvSharp/Mat
// involvement at all - classifiers only ever see these plain records.
internal static class FrameSignalSampleBuilder
{
    public static GlobalMotionEstimate NoMotion { get; } = Motion(0, 0, 0, 0);

    public static GlobalMotionEstimate Motion(double magnitude, double radialOutwardScore, double directionalConsistency, double meanDx = 0, double meanDy = 0) =>
        new()
        {
            MeanDx = meanDx,
            MeanDy = meanDy,
            Magnitude = magnitude,
            RadialOutwardScore = radialOutwardScore,
            DirectionalConsistency = directionalConsistency,
        };

    public static FrameSignalSample Sample(
        double index,
        double hsvHistogramDistance = 0,
        double luminanceDelta = 0,
        double edgeDensityDelta = 0,
        double laplacianBlurChange = 0,
        double blackScore = 0,
        double whiteScore = 0,
        double currentLaplacianVariance = 100,
        double currentEdgeDensity = 0.1,
        double structuralDifference = 0,
        GlobalMotionEstimate? globalMotion = null) => new()
        {
            PreviousTimestamp = TimeSpan.FromSeconds(index * 0.25),
            Timestamp = TimeSpan.FromSeconds((index + 1) * 0.25),
            HsvHistogramDistance = hsvHistogramDistance,
            LuminanceDelta = luminanceDelta,
            EdgeDensityDelta = edgeDensityDelta,
            LaplacianBlurChange = laplacianBlurChange,
            BlackScore = blackScore,
            WhiteScore = whiteScore,
            CurrentLaplacianVariance = currentLaplacianVariance,
            CurrentEdgeDensity = currentEdgeDensity,
            StructuralDifference = structuralDifference,
            GlobalMotion = globalMotion ?? Motion(0, 0, 0),
        };
}
