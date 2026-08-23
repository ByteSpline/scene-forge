using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Tests.Extraction;

public class CleanClipExtractionOptionsTests
{
    [Fact]
    public void ForProfile_DelegatesToFrameSamplingProfilesAndCarriesSceneRanges()
    {
        var sceneRanges = new[] { new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) };

        var options = CleanClipExtractionOptions.ForProfile(AnalysisProfile.Accurate, sceneRanges);

        Assert.Equal(FrameSamplingOptions.ForProfile(AnalysisProfile.Accurate), options.SamplingOptions);
        Assert.Same(sceneRanges, options.SceneRanges);
        Assert.Empty(options.ExcludedIntervals);
    }

    [Fact]
    public void ForProfile_NullExcludedIntervals_DefaultsToEmpty()
    {
        var options = CleanClipExtractionOptions.ForProfile(AnalysisProfile.Fast, [], null);

        Assert.Empty(options.ExcludedIntervals);
    }

    [Theory]
    [InlineData(0.1, 1.0)]
    [InlineData(20.0, 10.0)]
    public void MinClipDuration_ClampsToAllowedRange(double seconds, double expectedSeconds)
    {
        var options = new CleanClipScoringOptions { MinClipDuration = TimeSpan.FromSeconds(seconds) };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.MinClipDuration);
    }

    [Fact]
    public void BoundaryGuard_NegativeValue_ClampsToZero()
    {
        var options = new CleanClipScoringOptions { BoundaryGuard = TimeSpan.FromSeconds(-1) };

        Assert.Equal(TimeSpan.Zero, options.BoundaryGuard);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.99, 0.9)]
    public void OverlapFraction_ClampsToZeroToPointNine(double value, double expected)
    {
        var options = new CleanClipScoringOptions { OverlapFraction = value };

        Assert.Equal(expected, options.OverlapFraction);
    }

    [Fact]
    public void SharpnessWeight_Negative_ClampsToZero()
    {
        var options = new CleanClipScoringOptions { SharpnessWeight = -5 };

        Assert.Equal(0.0, options.SharpnessWeight);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    public void AcceptanceThreshold_ClampsToZeroToOne(double value, double expected)
    {
        var options = new CleanClipScoringOptions { AcceptanceThreshold = value };

        Assert.Equal(expected, options.AcceptanceThreshold);
    }

    [Fact]
    public void OverlayRatioReference_ValueAtOrBelowOne_ClampsAboveOne()
    {
        var options = new CleanClipScoringOptions { OverlayRatioReference = 1.0 };

        Assert.True(options.OverlayRatioReference > 1.0);
    }

    [Theory]
    [InlineData(-0.2, 0.0)]
    [InlineData(2.0, 1.0)]
    public void ClusteringOptions_SimilarityThreshold_ClampsToZeroToOne(double value, double expected)
    {
        var options = new ClusteringOptions { SimilarityThreshold = value };

        Assert.Equal(expected, options.SimilarityThreshold);
    }

    [Fact]
    public void Default_IsAccessibleAndReusable()
    {
        Assert.Same(CleanClipScoringOptions.Default, CleanClipScoringOptions.Default);
        Assert.Same(ClusteringOptions.Default, ClusteringOptions.Default);
    }
}
