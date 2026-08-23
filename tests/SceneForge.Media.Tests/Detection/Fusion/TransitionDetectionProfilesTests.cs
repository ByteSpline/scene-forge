using SceneForge.Media.Detection.Fusion;

namespace SceneForge.Media.Tests.Detection.Fusion;

public class TransitionDetectionProfilesTests
{
    [Fact]
    public void GetDefaults_V1_HasExpectedVersionAndPositiveWindow()
    {
        var profile = TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1);

        Assert.Equal(TransitionDetectionProfileVersion.V1, profile.Version);
        Assert.True(profile.MaxTransitionDuration > TimeSpan.Zero);
        Assert.NotNull(profile.HardCut);
        Assert.NotNull(profile.FadeBlack);
        Assert.NotNull(profile.Dissolve);
        Assert.NotNull(profile.Flash);
        Assert.NotNull(profile.BlurTransition);
        Assert.NotNull(profile.ZoomTransition);
        Assert.NotNull(profile.DirectionalSwipe);
    }

    [Fact]
    public void GetDefaults_UnknownVersion_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TransitionDetectionProfiles.GetDefaults((TransitionDetectionProfileVersion)999));
    }
}
