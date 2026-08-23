using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Classification;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Classification;

public class DissolveClassifierTests
{
    private readonly DissolveClassifier _classifier = new();
    private readonly TransitionDetectionProfile _profile = TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1);

    [Fact]
    public void Classify_BellShapedElevation_DetectsDissolve()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, structuralDifference: 0.03),
            FrameSignalSampleBuilder.Sample(1, structuralDifference: 0.10),
            FrameSignalSampleBuilder.Sample(2, structuralDifference: 0.25),
            FrameSignalSampleBuilder.Sample(3, structuralDifference: 0.10),
            FrameSignalSampleBuilder.Sample(4, structuralDifference: 0.03),
        };

        var results = _classifier.Classify(window, _profile);

        var candidate = Assert.Single(results);
        Assert.Equal(TransitionType.Dissolve, candidate.Type);
        Assert.Equal(window[0].PreviousTimestamp, candidate.Start);
        Assert.Equal(window[2].Timestamp, candidate.Peak);
        Assert.Equal(window[4].Timestamp, candidate.End);
    }

    [Fact]
    public void Classify_BellShapeThroughBlackFrame_DoesNotDetectDissolve()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, structuralDifference: 0.03),
            FrameSignalSampleBuilder.Sample(1, structuralDifference: 0.10),
            FrameSignalSampleBuilder.Sample(2, structuralDifference: 0.25, blackScore: 0.9),
            FrameSignalSampleBuilder.Sample(3, structuralDifference: 0.10),
            FrameSignalSampleBuilder.Sample(4, structuralDifference: 0.03),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }

    [Fact]
    public void Classify_TooShortElevation_DoesNotDetectDissolve()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, structuralDifference: 0.02),
            FrameSignalSampleBuilder.Sample(1, structuralDifference: 0.25),
            FrameSignalSampleBuilder.Sample(2, structuralDifference: 0.02),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }
}
