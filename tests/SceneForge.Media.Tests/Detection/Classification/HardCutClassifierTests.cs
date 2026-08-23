using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Classification;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Classification;

public class HardCutClassifierTests
{
    private readonly HardCutClassifier _classifier = new();
    private readonly TransitionDetectionProfile _profile = TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1);

    [Fact]
    public void Classify_IsolatedSpike_DetectsHardCut()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, structuralDifference: 0.02, hsvHistogramDistance: 0.02),
            FrameSignalSampleBuilder.Sample(1, structuralDifference: 0.5, hsvHistogramDistance: 0.5),
            FrameSignalSampleBuilder.Sample(2, structuralDifference: 0.02, hsvHistogramDistance: 0.02),
        };

        var results = _classifier.Classify(window, _profile);

        var candidate = Assert.Single(results);
        Assert.Equal(TransitionType.HardCut, candidate.Type);
        Assert.Equal(window[1].PreviousTimestamp, candidate.Start);
        Assert.Equal(window[1].Timestamp, candidate.End);
        Assert.Equal(window[1].Timestamp, candidate.Peak);
        Assert.InRange(candidate.Confidence, 0.0, 1.0);
        Assert.NotEmpty(candidate.DiagnosticReason);
    }

    [Fact]
    public void Classify_SustainedElevation_DoesNotDetectHardCut()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, structuralDifference: 0.4, hsvHistogramDistance: 0.4),
            FrameSignalSampleBuilder.Sample(1, structuralDifference: 0.4, hsvHistogramDistance: 0.4),
            FrameSignalSampleBuilder.Sample(2, structuralDifference: 0.4, hsvHistogramDistance: 0.4),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }

    [Fact]
    public void Classify_IsolatedStructuralSpikeAlone_DetectsHardCut()
    {
        // Only StructuralDifference crosses its threshold (e.g. a
        // same-hue, brightness-only cut on desaturated content, where
        // HsvHistogramDistance stays near zero regardless of luma) -
        // the two signals must gate independently, not as an AND.
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, structuralDifference: 0.02, hsvHistogramDistance: 0.01),
            FrameSignalSampleBuilder.Sample(1, structuralDifference: 0.6, hsvHistogramDistance: 0.01),
            FrameSignalSampleBuilder.Sample(2, structuralDifference: 0.02, hsvHistogramDistance: 0.01),
        };

        var results = _classifier.Classify(window, _profile);

        var candidate = Assert.Single(results);
        Assert.Equal(TransitionType.HardCut, candidate.Type);
    }

    [Fact]
    public void Classify_IsolatedHsvSpikeAlone_DetectsHardCut()
    {
        // Only HsvHistogramDistance crosses its threshold (e.g. a
        // same-luma, hue-only cut).
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, structuralDifference: 0.01, hsvHistogramDistance: 0.02),
            FrameSignalSampleBuilder.Sample(1, structuralDifference: 0.01, hsvHistogramDistance: 0.6),
            FrameSignalSampleBuilder.Sample(2, structuralDifference: 0.01, hsvHistogramDistance: 0.02),
        };

        var results = _classifier.Classify(window, _profile);

        var candidate = Assert.Single(results);
        Assert.Equal(TransitionType.HardCut, candidate.Type);
    }

    [Fact]
    public void Classify_BelowThreshold_DetectsNothing()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, structuralDifference: 0.1, hsvHistogramDistance: 0.1),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }
}
