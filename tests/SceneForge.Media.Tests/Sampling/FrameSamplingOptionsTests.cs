using SceneForge.Media.Sampling;

namespace SceneForge.Media.Tests.Sampling;

public class FrameSamplingOptionsTests
{
    [Fact]
    public void AnalysisWidthPixels_BelowMinimum_ClampsToMinimum()
    {
        var options = new FrameSamplingOptions { AnalysisWidthPixels = 1 };

        Assert.Equal(16, options.AnalysisWidthPixels);
    }

    [Fact]
    public void AnalysisWidthPixels_AboveMaximum_ClampsToMaximum()
    {
        var options = new FrameSamplingOptions { AnalysisWidthPixels = 100_000 };

        Assert.Equal(4096, options.AnalysisWidthPixels);
    }

    [Fact]
    public void SampleFramesPerSecond_NonPositive_ClampsToMinimum()
    {
        var options = new FrameSamplingOptions { SampleFramesPerSecond = -5 };

        Assert.Equal(0.1, options.SampleFramesPerSecond);
    }

    [Fact]
    public void SampleFramesPerSecond_AboveMaximum_ClampsToMaximum()
    {
        var options = new FrameSamplingOptions { SampleFramesPerSecond = 1000 };

        Assert.Equal(60.0, options.SampleFramesPerSecond);
    }

    [Fact]
    public void ChannelCapacity_BelowMinimum_ClampsToMinimum()
    {
        var options = new FrameSamplingOptions { ChannelCapacity = 0 };

        Assert.Equal(1, options.ChannelCapacity);
    }

    [Fact]
    public void ChannelCapacity_AboveMaximum_ClampsToMaximum()
    {
        var options = new FrameSamplingOptions { ChannelCapacity = 1000 };

        Assert.Equal(64, options.ChannelCapacity);
    }

    [Fact]
    public void Defaults_MatchBalancedProfile()
    {
        var options = new FrameSamplingOptions();

        Assert.Equal(FrameSamplingProfiles.BalancedAnalysisWidthPixels, options.AnalysisWidthPixels);
        Assert.Equal(FrameSamplingProfiles.BalancedSampleFramesPerSecond, options.SampleFramesPerSecond);
        Assert.Equal(FrameSamplePixelFormat.Bgr24, options.PixelFormat);
        Assert.False(options.IncludeExpensiveSignals);
    }

    [Fact]
    public void ForProfile_DelegatesToFrameSamplingProfiles()
    {
        var viaOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Accurate);
        var viaProfiles = FrameSamplingProfiles.GetDefaults(AnalysisProfile.Accurate);

        Assert.Equal(viaProfiles, viaOptions);
    }
}
