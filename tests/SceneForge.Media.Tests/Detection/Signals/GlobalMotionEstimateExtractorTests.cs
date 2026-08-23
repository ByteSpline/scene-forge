using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Signals;

public class GlobalMotionEstimateExtractorTests
{
    private readonly GlobalMotionEstimateExtractor _extractor = new();

    // RadialOutwardScore/DirectionalConsistency are cosine-similarity scores
    // over near-zero-length noise vectors when there is no real motion, so
    // they are not asserted here beyond being finite/in-range - only
    // Magnitude is a reliable "no motion" signal. This is why
    // FrameSignalSample documents that callers must gate on Magnitude
    // (via a profile's MinMotionMagnitude) before trusting either score.
    [Fact]
    public void Extract_IdenticalFrames_HasNearZeroMagnitude()
    {
        using var frameA = FrameSampleBuilder.TexturedShift(shiftX: 0);
        using var frameB = FrameSampleBuilder.TexturedShift(shiftX: 0);
        using var analyzedA = AnalyzedFrame.Create(frameA);
        using var analyzedB = AnalyzedFrame.Create(frameB);

        var motion = _extractor.Extract(analyzedA, analyzedB);

        Assert.InRange(motion.Magnitude, 0.0, 0.01);
        Assert.InRange(motion.RadialOutwardScore, -1.0, 1.0);
        Assert.InRange(motion.DirectionalConsistency, 0.0, 1.0);
    }

    [Fact]
    public void Extract_OppositeShiftDirections_ProduceOppositeSignedMeanDx()
    {
        using var baseline = FrameSampleBuilder.TexturedShift(shiftX: 0);
        using var shiftedRight = FrameSampleBuilder.TexturedShift(shiftX: 6);
        using var shiftedLeft = FrameSampleBuilder.TexturedShift(shiftX: -6);
        using var analyzedBaseline = AnalyzedFrame.Create(baseline);
        using var analyzedShiftedRight = AnalyzedFrame.Create(shiftedRight);
        using var analyzedShiftedLeft = AnalyzedFrame.Create(shiftedLeft);

        var motionRight = _extractor.Extract(analyzedBaseline, analyzedShiftedRight);
        var motionLeft = _extractor.Extract(analyzedBaseline, analyzedShiftedLeft);

        Assert.True(motionRight.Magnitude > 0.005);
        Assert.True(motionLeft.Magnitude > 0.005);
        Assert.True(Math.Sign(motionRight.MeanDx) != Math.Sign(motionLeft.MeanDx));
    }

    [Fact]
    public void Extract_UniformTranslation_HasHighDirectionalConsistency()
    {
        using var baseline = FrameSampleBuilder.TexturedShift(shiftX: 0);
        using var shifted = FrameSampleBuilder.TexturedShift(shiftX: 6);
        using var analyzedBaseline = AnalyzedFrame.Create(baseline);
        using var analyzedShifted = AnalyzedFrame.Create(shifted);

        var motion = _extractor.Extract(analyzedBaseline, analyzedShifted);

        Assert.True(motion.DirectionalConsistency > 0.6);
    }

    [Fact]
    public void Extract_LargerShift_HasGreaterMagnitudeThanSmallerShift()
    {
        using var baseline = FrameSampleBuilder.TexturedShift(shiftX: 0);
        using var smallShift = FrameSampleBuilder.TexturedShift(shiftX: 2);
        using var largeShift = FrameSampleBuilder.TexturedShift(shiftX: 6);
        using var analyzedBaseline = AnalyzedFrame.Create(baseline);
        using var analyzedSmallShift = AnalyzedFrame.Create(smallShift);
        using var analyzedLargeShift = AnalyzedFrame.Create(largeShift);

        var smallMotion = _extractor.Extract(analyzedBaseline, analyzedSmallShift);
        var largeMotion = _extractor.Extract(analyzedBaseline, analyzedLargeShift);

        Assert.True(largeMotion.Magnitude > smallMotion.Magnitude);
    }
}
