using SceneForge.Media.Detection.Fusion;

namespace SceneForge.Media.Tests.Detection.Fusion;

public class TransitionDetectionProfileTests
{
    private static TransitionDetectionProfile BaseProfile() => TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1);

    [Fact]
    public void MaxTransitionDuration_BelowMinimum_ClampsToMinimum()
    {
        var profile = BaseProfile() with { MaxTransitionDuration = TimeSpan.FromMilliseconds(1) };

        Assert.Equal(TimeSpan.FromMilliseconds(200), profile.MaxTransitionDuration);
    }

    [Fact]
    public void MaxTransitionDuration_AboveMaximum_ClampsToMaximum()
    {
        var profile = BaseProfile() with { MaxTransitionDuration = TimeSpan.FromSeconds(999) };

        Assert.Equal(TimeSpan.FromSeconds(10), profile.MaxTransitionDuration);
    }

    [Fact]
    public void PreBufferDuration_Negative_ClampsToZero()
    {
        var profile = BaseProfile() with { PreBufferDuration = TimeSpan.FromSeconds(-1) };

        Assert.Equal(TimeSpan.Zero, profile.PreBufferDuration);
    }

    [Fact]
    public void PostBufferDuration_AboveMaximum_ClampsToMaximum()
    {
        var profile = BaseProfile() with { PostBufferDuration = TimeSpan.FromSeconds(999) };

        Assert.Equal(TimeSpan.FromSeconds(2), profile.PostBufferDuration);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(2.0, 1.0)]
    public void HardCutThresholds_OutOfRangeRatios_ClampToUnitInterval(double input, double expected)
    {
        var thresholds = new HardCutThresholds
        {
            MinStructuralDifference = input,
            MinHsvHistogramDistance = input,
        };

        Assert.Equal(expected, thresholds.MinStructuralDifference);
        Assert.Equal(expected, thresholds.MinHsvHistogramDistance);
    }

    [Fact]
    public void FlashThresholds_MaxDuration_ClampsWithinAllowedRange()
    {
        var tooShort = new FlashThresholds { MaxDuration = TimeSpan.FromMilliseconds(1) };
        var tooLong = new FlashThresholds { MaxDuration = TimeSpan.FromSeconds(999) };

        Assert.Equal(TimeSpan.FromMilliseconds(20), tooShort.MaxDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), tooLong.MaxDuration);
    }

    [Fact]
    public void ZoomTransitionThresholds_NegativeMotionMagnitude_ClampsToZero()
    {
        var thresholds = new ZoomTransitionThresholds { MinMotionMagnitude = -5 };

        Assert.Equal(0.0, thresholds.MinMotionMagnitude);
    }

    [Fact]
    public void DirectionalSwipeThresholds_ConsistencyAboveOne_ClampsToOne()
    {
        var thresholds = new DirectionalSwipeThresholds { MinDirectionalConsistency = 5.0 };

        Assert.Equal(1.0, thresholds.MinDirectionalConsistency);
    }
}
