using SceneForge.Media.Extraction;
using SceneForge.Media.Extraction.Clustering;

namespace SceneForge.Media.Tests.Extraction.Clustering;

public class VisualClustererTests
{
    private static PerceptualDescriptor Descriptor(ulong hash, float color = 0.5f, MotionClass motion = MotionClass.Static) => new()
    {
        PerceptualHash = hash,
        ColorHistogram = [color, 1 - color],
        EdgeHistogram = [0.1f, 0.1f],
        Motion = motion,
    };

    [Fact]
    public void Cluster_EmptyInput_ReturnsNoClusters()
    {
        Assert.Empty(VisualClusterer.Cluster([], ClusteringOptions.Default));
    }

    [Fact]
    public void Cluster_SingleDescriptor_ReturnsOneClusterContainingIt()
    {
        var clusters = VisualClusterer.Cluster([Descriptor(0x1)], ClusteringOptions.Default);

        var cluster = Assert.Single(clusters);
        Assert.Equal(0, cluster.ClusterId);
        Assert.Equal([0], cluster.MemberClipIndices);
    }

    [Fact]
    public void Cluster_TwoNearIdenticalDescriptors_JoinTheSameCluster()
    {
        var descriptors = new[] { Descriptor(0x1234), Descriptor(0x1234) };

        var clusters = VisualClusterer.Cluster(descriptors, ClusteringOptions.Default);

        var cluster = Assert.Single(clusters);
        Assert.Equal([0, 1], cluster.MemberClipIndices);
    }

    [Fact]
    public void Cluster_TwoCompletelyDifferentDescriptors_FormSeparateClusters()
    {
        var descriptors = new[] { Descriptor(0, color: 1.0f, motion: MotionClass.Static), Descriptor(ulong.MaxValue, color: 0.0f, motion: MotionClass.High) };

        var clusters = VisualClusterer.Cluster(descriptors, ClusteringOptions.Default);

        Assert.Equal(2, clusters.Count);
        Assert.Equal([0], clusters[0].MemberClipIndices);
        Assert.Equal([1], clusters[1].MemberClipIndices);
    }

    [Fact]
    public void Cluster_RepresentativeIsTheClusterLeaderDescriptor()
    {
        var leader = Descriptor(0x1234);
        var descriptors = new[] { leader, Descriptor(0x1234) };

        var clusters = VisualClusterer.Cluster(descriptors, ClusteringOptions.Default);

        var cluster = Assert.Single(clusters);
        Assert.Equal(leader, cluster.Representative);
    }

    [Fact]
    public void Cluster_ThreeGroupsOfSimilarDescriptors_ProducesThreeClusters()
    {
        var descriptors = new[]
        {
            Descriptor(0x00, color: 0.9f),
            Descriptor(0x00, color: 0.9f),
            Descriptor(0xFF, color: 0.1f),
            Descriptor(0xFF, color: 0.1f),
            Descriptor(0xF0F0F0F0F0F0F0F0, color: 0.5f),
        };

        var clusters = VisualClusterer.Cluster(descriptors, ClusteringOptions.Default);

        Assert.Equal(3, clusters.Count);
        Assert.Equal(5, clusters.Sum(c => c.MemberClipIndices.Count));
    }

    [Fact]
    public void Cluster_IsDeterministicForTheSameInputOrder()
    {
        var descriptors = new[] { Descriptor(0x1), Descriptor(0xFFFFFF), Descriptor(0x2) };

        var first = VisualClusterer.Cluster(descriptors, ClusteringOptions.Default);
        var second = VisualClusterer.Cluster(descriptors, ClusteringOptions.Default);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].MemberClipIndices, second[i].MemberClipIndices);
        }
    }
}
