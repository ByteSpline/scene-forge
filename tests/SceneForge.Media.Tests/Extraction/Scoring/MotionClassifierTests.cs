using SceneForge.Media.Extraction;
using SceneForge.Media.Extraction.Scoring;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Extraction.Scoring;

public class MotionClassifierTests
{
    private static readonly CleanClipScoringOptions Options = new() { StabilityReferenceValue = 0.1 };

    [Theory]
    [InlineData(0.0, MotionClass.Static)]
    [InlineData(0.01, MotionClass.Static)]
    [InlineData(0.03, MotionClass.Subtle)]
    [InlineData(0.07, MotionClass.Moderate)]
    [InlineData(0.2, MotionClass.High)]
    public void Classify_BucketsByRatioToStabilityReference(double meanStructuralDifference, MotionClass expected)
    {
        Assert.Equal(expected, MotionClassifier.Classify(meanStructuralDifference, Options));
    }

    [Fact]
    public void MeanStructuralDifference_EmptyFrames_ReturnsZero()
    {
        Assert.Equal(0.0, MotionClassifier.MeanStructuralDifference([]));
    }

    [Fact]
    public void MeanStructuralDifference_AveragesAcrossAllFrames()
    {
        var frames = new[]
        {
            ClipFrameMetricsBuilder.Sample(0, structuralDifferenceFromPrevious: 0.1),
            ClipFrameMetricsBuilder.Sample(1, structuralDifferenceFromPrevious: 0.3),
        };

        Assert.Equal(0.2, MotionClassifier.MeanStructuralDifference(frames), precision: 10);
    }
}
