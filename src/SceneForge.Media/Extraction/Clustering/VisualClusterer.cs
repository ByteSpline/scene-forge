namespace SceneForge.Media.Extraction.Clustering;

// Deterministic, no-heavy-ML-model clustering: a single greedy "leader"
// pass over descriptors in input order. Each descriptor joins the first
// existing cluster whose leader (its first, earliest-added member) is
// within ClusteringOptions.SimilarityThreshold; otherwise it starts a new
// cluster as its own leader. O(clusters x candidates), which for the
// bounded number of accepted clips a single video produces is trivial -
// no external ML/embedding model or GPU involved anywhere.
internal static class VisualClusterer
{
    public static IReadOnlyList<VisualCluster> Cluster(IReadOnlyList<PerceptualDescriptor> descriptors, ClusteringOptions options)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(options);

        var clusters = new List<(PerceptualDescriptor Leader, List<int> Members)>();

        for (var i = 0; i < descriptors.Count; i++)
        {
            var descriptor = descriptors[i];
            var joinedClusterIndex = -1;

            for (var c = 0; c < clusters.Count; c++)
            {
                if (PerceptualDistance.Compute(clusters[c].Leader, descriptor, options) <= options.SimilarityThreshold)
                {
                    joinedClusterIndex = c;
                    break;
                }
            }

            if (joinedClusterIndex >= 0)
            {
                clusters[joinedClusterIndex].Members.Add(i);
            }
            else
            {
                clusters.Add((descriptor, [i]));
            }
        }

        var result = new List<VisualCluster>(clusters.Count);
        for (var clusterId = 0; clusterId < clusters.Count; clusterId++)
        {
            result.Add(new VisualCluster
            {
                ClusterId = clusterId,
                MemberClipIndices = clusters[clusterId].Members,
                Representative = clusters[clusterId].Leader,
            });
        }

        return result;
    }
}
