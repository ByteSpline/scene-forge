using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Classification;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Classification;

public class FadeBlackClassifierTests
{
    private readonly FadeBlackClassifier _classifier = new();
    private readonly TransitionDetectionProfile _profile = TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1);

    [Fact]
    public void Classify_DarkeningRampToBlack_DetectsFadeToBlack()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, blackScore: 0.1, luminanceDelta: 0),
            FrameSignalSampleBuilder.Sample(1, blackScore: 0.4, luminanceDelta: -0.3),
            FrameSignalSampleBuilder.Sample(2, blackScore: 0.85, luminanceDelta: -0.4),
        };

        var results = _classifier.Classify(window, _profile);

        var candidate = Assert.Single(results, c => c.Type == TransitionType.FadeToBlack);
        Assert.Equal(window[0].PreviousTimestamp, candidate.Start);
        Assert.Equal(window[2].Timestamp, candidate.Peak);
        Assert.Equal(window[2].Timestamp, candidate.End);
    }

    [Fact]
    public void Classify_LighteningRampFromBlack_DetectsFadeFromBlack()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, blackScore: 0.85, luminanceDelta: 0),
            FrameSignalSampleBuilder.Sample(1, blackScore: 0.4, luminanceDelta: 0.4),
            FrameSignalSampleBuilder.Sample(2, blackScore: 0.1, luminanceDelta: 0.3),
        };

        var results = _classifier.Classify(window, _profile);

        var candidate = Assert.Single(results, c => c.Type == TransitionType.FadeFromBlack);
        Assert.Equal(window[0].Timestamp, candidate.Start);
        Assert.Equal(window[0].Timestamp, candidate.Peak);
        Assert.Equal(window[2].Timestamp, candidate.End);
    }

    [Fact]
    public void Classify_PeakBelowThreshold_DetectsNothing()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, blackScore: 0.1, luminanceDelta: 0),
            FrameSignalSampleBuilder.Sample(1, blackScore: 0.3, luminanceDelta: -0.1),
            FrameSignalSampleBuilder.Sample(2, blackScore: 0.5, luminanceDelta: -0.1),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }
}
