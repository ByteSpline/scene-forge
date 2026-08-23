using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Classification;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Classification;

public class FlashClassifierTests
{
    private readonly FlashClassifier _classifier = new();
    private readonly TransitionDetectionProfile _profile = TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1);

    [Fact]
    public void Classify_BriefWhiteSpike_DetectsFlash()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, whiteScore: 0.1),
            FrameSignalSampleBuilder.Sample(1, whiteScore: 0.9),
            FrameSignalSampleBuilder.Sample(2, whiteScore: 0.1),
        };

        var results = _classifier.Classify(window, _profile);

        var candidate = Assert.Single(results);
        Assert.Equal(TransitionType.Flash, candidate.Type);
        Assert.True(candidate.End - candidate.Start <= _profile.Flash.MaxDuration);
    }

    [Fact]
    public void Classify_SustainedWhiteSpanLongerThanMaxDuration_DoesNotDetectFlash()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, whiteScore: 0.1),
            FrameSignalSampleBuilder.Sample(1, whiteScore: 0.9),
            FrameSignalSampleBuilder.Sample(2, whiteScore: 0.9),
            FrameSignalSampleBuilder.Sample(3, whiteScore: 0.9),
            FrameSignalSampleBuilder.Sample(4, whiteScore: 0.9),
            FrameSignalSampleBuilder.Sample(5, whiteScore: 0.9),
            FrameSignalSampleBuilder.Sample(6, whiteScore: 0.1),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }

    [Fact]
    public void Classify_PeakBelowThreshold_DetectsNothing()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, whiteScore: 0.1),
            FrameSignalSampleBuilder.Sample(1, whiteScore: 0.4),
            FrameSignalSampleBuilder.Sample(2, whiteScore: 0.1),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }
}
