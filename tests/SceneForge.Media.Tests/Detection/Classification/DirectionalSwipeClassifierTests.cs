using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Classification;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Classification;

public class DirectionalSwipeClassifierTests
{
    private readonly DirectionalSwipeClassifier _classifier = new();
    private readonly TransitionDetectionProfile _profile = TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1);

    [Fact]
    public void Classify_SustainedUniformDirection_DetectsDirectionalSwipe()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, globalMotion: FrameSignalSampleBuilder.Motion(0.1, 0.1, 0.9)),
            FrameSignalSampleBuilder.Sample(1, globalMotion: FrameSignalSampleBuilder.Motion(0.15, 0.1, 0.95)),
            FrameSignalSampleBuilder.Sample(2, globalMotion: FrameSignalSampleBuilder.Motion(0.1, 0.1, 0.9)),
        };

        var results = _classifier.Classify(window, _profile);

        var candidate = Assert.Single(results);
        Assert.Equal(TransitionType.DirectionalSwipe, candidate.Type);
        Assert.Equal(window[0].PreviousTimestamp, candidate.Start);
        Assert.Equal(window[2].Timestamp, candidate.End);
    }

    [Fact]
    public void Classify_LowDirectionalConsistency_DetectsNothing()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, globalMotion: FrameSignalSampleBuilder.Motion(0.1, 0.1, 0.2)),
            FrameSignalSampleBuilder.Sample(1, globalMotion: FrameSignalSampleBuilder.Motion(0.1, 0.1, 0.2)),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }

    [Fact]
    public void Classify_MagnitudeBelowThreshold_DetectsNothing()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, globalMotion: FrameSignalSampleBuilder.Motion(0.001, 0.1, 0.95)),
            FrameSignalSampleBuilder.Sample(1, globalMotion: FrameSignalSampleBuilder.Motion(0.001, 0.1, 0.95)),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }
}
