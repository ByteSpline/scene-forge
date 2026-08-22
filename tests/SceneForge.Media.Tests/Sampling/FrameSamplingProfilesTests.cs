using SceneForge.Media.Sampling;

namespace SceneForge.Media.Tests.Sampling;

public class FrameSamplingProfilesTests
{
    [Fact]
    public void GetDefaults_Fast_Is320PixelsAt2Fps()
    {
        var options = FrameSamplingProfiles.GetDefaults(AnalysisProfile.Fast);

        Assert.Equal(320, options.AnalysisWidthPixels);
        Assert.Equal(2.0, options.SampleFramesPerSecond);
        Assert.False(options.IncludeExpensiveSignals);
    }

    [Fact]
    public void GetDefaults_Balanced_Is384PixelsAt4Fps()
    {
        var options = FrameSamplingProfiles.GetDefaults(AnalysisProfile.Balanced);

        Assert.Equal(384, options.AnalysisWidthPixels);
        Assert.Equal(4.0, options.SampleFramesPerSecond);
        Assert.False(options.IncludeExpensiveSignals);
    }

    [Fact]
    public void GetDefaults_Accurate_Is480PixelsAt8FpsWithExpensiveSignals()
    {
        var options = FrameSamplingProfiles.GetDefaults(AnalysisProfile.Accurate);

        Assert.Equal(480, options.AnalysisWidthPixels);
        Assert.Equal(8.0, options.SampleFramesPerSecond);
        Assert.True(options.IncludeExpensiveSignals);
    }

    [Fact]
    public void GetDefaults_UnknownProfile_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameSamplingProfiles.GetDefaults((AnalysisProfile)999));
    }
}
