namespace SceneForge.Media.Extraction;

// A group of accepted CleanClips whose PerceptualDescriptors were close
// enough (see VisualClusterer/PerceptualDistance) to be considered
// near-duplicates of the same visual content - e.g. several candidates
// drawn from one long, static talking-head shot. MemberClipIndices index
// into the AcceptedClips list VisualClusterer was called with; Representative
// is the descriptor of the first (lowest-index, i.e. earliest by Range.Start)
// member - a deterministic "leader", not a computed centroid.
public sealed record VisualCluster
{
    public required int ClusterId { get; init; }

    public required IReadOnlyList<int> MemberClipIndices { get; init; }

    public required PerceptualDescriptor Representative { get; init; }
}
