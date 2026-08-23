using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Tests.Detection;

public class TransitionDetectionOptionsTests
{
    [Fact]
    public void ForProfile_DelegatesToFrameSamplingAndDetectionProfiles()
    {
        var options = TransitionDetectionOptions.ForProfile(AnalysisProfile.Accurate);

        Assert.Equal(FrameSamplingOptions.ForProfile(AnalysisProfile.Accurate), options.SamplingOptions);
        Assert.Equal(TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1), options.Profile);
    }

    [Fact]
    public void ForProfile_HonorsExplicitDetectionProfileVersion()
    {
        var options = TransitionDetectionOptions.ForProfile(AnalysisProfile.Fast, TransitionDetectionProfileVersion.V1);

        Assert.Equal(TransitionDetectionProfileVersion.V1, options.Profile.Version);
    }
}
