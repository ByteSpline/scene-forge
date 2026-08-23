using SceneForge.Media.Extraction;
using SceneForge.Media.Extraction.Clustering;

namespace SceneForge.Media.Tests.Extraction.Clustering;

public class PerceptualDistanceTests
{
    private static readonly ClusteringOptions Options = ClusteringOptions.Default;

    private static PerceptualDescriptor Descriptor(
        ulong hash,
        float[]? colorHistogram = null,
        float[]? edgeHistogram = null,
        MotionClass motion = MotionClass.Static) => new()
        {
            PerceptualHash = hash,
            ColorHistogram = colorHistogram ?? [0.5f, 0.5f],
            EdgeHistogram = edgeHistogram ?? [0.1f, 0.1f],
            Motion = motion,
        };

    [Fact]
    public void Compute_IdenticalDescriptors_ReturnsZero()
    {
        var a = Descriptor(0x1234);
        var b = Descriptor(0x1234);

        Assert.Equal(0.0, PerceptualDistance.Compute(a, b, Options));
    }

    [Fact]
    public void Compute_DifferentHash_ScalesWithHammingDistance()
    {
        var a = Descriptor(0);
        var oneBit = Descriptor(0b1);
        var allBits = Descriptor(ulong.MaxValue);

        var distanceOneBit = PerceptualDistance.Compute(a, oneBit, Options);
        var distanceAllBits = PerceptualDistance.Compute(a, allBits, Options);

        Assert.True(distanceOneBit > 0);
        Assert.True(distanceAllBits > distanceOneBit);
    }

    [Fact]
    public void Compute_DifferentColorHistograms_IncreasesDistance()
    {
        var a = Descriptor(0, colorHistogram: [1.0f, 0.0f]);
        var b = Descriptor(0, colorHistogram: [0.0f, 1.0f]);

        Assert.True(PerceptualDistance.Compute(a, b, Options) > 0);
    }

    [Fact]
    public void Compute_MismatchedMotionClass_AddsFlatPenalty()
    {
        var a = Descriptor(0, motion: MotionClass.Static);
        var sameMotion = Descriptor(0, motion: MotionClass.Static);
        var differentMotion = Descriptor(0, motion: MotionClass.High);

        var distanceSame = PerceptualDistance.Compute(a, sameMotion, Options);
        var distanceDifferent = PerceptualDistance.Compute(a, differentMotion, Options);

        Assert.Equal(distanceSame + Options.MotionMismatchPenalty, distanceDifferent, precision: 10);
    }

    [Fact]
    public void Compute_EmptyHistograms_TreatedAsMaximallyDissimilarRatherThanThrowing()
    {
        var a = Descriptor(0, colorHistogram: [], edgeHistogram: []);
        var b = Descriptor(0, colorHistogram: [0.5f, 0.5f], edgeHistogram: [0.1f, 0.1f]);

        var distance = PerceptualDistance.Compute(a, b, Options);

        Assert.True(distance > 0);
    }
}
