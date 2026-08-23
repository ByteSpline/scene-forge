using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Classification;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Classification;

public class BlurTransitionClassifierTests
{
    private readonly BlurTransitionClassifier _classifier = new();
    private readonly TransitionDetectionProfile _profile = TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1);

    [Fact]
    public void Classify_SharpToBlurredToSharp_DetectsBlurTransition()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, currentLaplacianVariance: 100, currentEdgeDensity: 0.30),
            FrameSignalSampleBuilder.Sample(1, currentLaplacianVariance: 40, currentEdgeDensity: 0.05),
            FrameSignalSampleBuilder.Sample(2, currentLaplacianVariance: 35, currentEdgeDensity: 0.04),
            FrameSignalSampleBuilder.Sample(3, currentLaplacianVariance: 95, currentEdgeDensity: 0.28),
        };

        var results = _classifier.Classify(window, _profile);

        var candidate = Assert.Single(results);
        Assert.Equal(TransitionType.BlurTransition, candidate.Type);
        Assert.Equal(window[1].PreviousTimestamp, candidate.Start);
        Assert.Equal(window[2].Timestamp, candidate.Peak);
        Assert.Equal(window[2].Timestamp, candidate.End);
    }

    [Fact]
    public void Classify_ConsistentlySharp_DetectsNothing()
    {
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, currentLaplacianVariance: 100, currentEdgeDensity: 0.30),
            FrameSignalSampleBuilder.Sample(1, currentLaplacianVariance: 98, currentEdgeDensity: 0.29),
            FrameSignalSampleBuilder.Sample(2, currentLaplacianVariance: 102, currentEdgeDensity: 0.31),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }

    [Fact]
    public void Classify_VarianceDropWithoutEdgeDensityDrop_DetectsNothing()
    {
        // Variance drops enough, but edge density stays flat - not a genuine
        // blur (a real blur must also lose fine edges).
        var window = new[]
        {
            FrameSignalSampleBuilder.Sample(0, currentLaplacianVariance: 100, currentEdgeDensity: 0.200),
            FrameSignalSampleBuilder.Sample(1, currentLaplacianVariance: 40, currentEdgeDensity: 0.196),
            FrameSignalSampleBuilder.Sample(2, currentLaplacianVariance: 35, currentEdgeDensity: 0.195),
            FrameSignalSampleBuilder.Sample(3, currentLaplacianVariance: 95, currentEdgeDensity: 0.200),
        };

        var results = _classifier.Classify(window, _profile);

        Assert.Empty(results);
    }
}
