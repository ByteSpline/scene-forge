using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Classification;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Classification;

public class ZoomTransitionClassifierTests
{
    private readonly ZoomTransitionClassifier _classifier = new();
    private readonly TransitionDetectionProfile _profile = TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1);

    [Fact]
    public void Classify_SustainedOutwardRadialFlow_DetectsZoomTransition()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, globalMotion: FrameSignalSampleBuilder.Motion(0.1, 0.6, 0.2)),
            FrameSignalSampleBuilder.Sample(1, globalMotion: FrameSignalSampleBuilder.Motion(0.15, 0.7, 0.2)),
            FrameSignalSampleBuilder.Sample(2, globalMotion: FrameSignalSampleBuilder.Motion(0.1, 0.6, 0.2)),
        };

        var results = _classifier.Classify(window, _profile);

        var candidate = Assert.Single(results);
        Assert.Equal(TransitionType.ZoomTransition, candidate.Type);
        Assert.Equal(window[0].PreviousTimestamp, candidate.Start);
        Assert.Equal(window[2].Timestamp, candidate.End);
    }

    [Fact]
    public void Classify_SignFlipMidRun_SplitsIntoTwoCandidates()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, globalMotion: FrameSignalSampleBuilder.Motion(0.1, 0.6, 0.2)),
            FrameSignalSampleBuilder.Sample(1, globalMotion: FrameSignalSampleBuilder.Motion(0.1, 0.6, 0.2)),
            FrameSignalSampleBuilder.Sample(2, globalMotion: FrameSignalSampleBuilder.Motion(0.1, -0.6, 0.2)),
            FrameSignalSampleBuilder.Sample(3, globalMotion: FrameSignalSampleBuilder.Motion(0.1, -0.6, 0.2)),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, c => c.Start == window[0].PreviousTimestamp && c.End == window[1].Timestamp);
        Assert.Contains(results, c => c.Start == window[2].PreviousTimestamp && c.End == window[3].Timestamp);
    }

    [Fact]
    public void Classify_MagnitudeBelowThreshold_DetectsNothing()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, globalMotion: FrameSignalSampleBuilder.Motion(0.001, 0.9, 0.2)),
            FrameSignalSampleBuilder.Sample(1, globalMotion: FrameSignalSampleBuilder.Motion(0.001, 0.9, 0.2)),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }
}
