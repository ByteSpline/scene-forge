using SceneForge.Media.Domain;

namespace SceneForge.Media.Extraction;

// Everything one CleanClipExtractor.ExtractAsync run produced, kept for
// manual inspection rather than collapsing straight down to "the accepted
// clips": RemainingCleanRanges shows what interval subtraction actually
// left to work with, and RejectedClips (with their ClipScore.Reasons) shows
// exactly why a candidate did not make the cut - never silently dropped.
public sealed record CleanClipExtractionResult
{
    // Scene ranges after subtracting every excluded interval, before any
    // candidate generation or scoring - the direct output of
    // IntervalSubtractor.
    public required IReadOnlyList<TimeRange> RemainingCleanRanges { get; init; }

    public required IReadOnlyList<CleanClip> AcceptedClips { get; init; }

    public required IReadOnlyList<CleanClip> RejectedClips { get; init; }

    // Clusters over AcceptedClips only - rejected candidates are never
    // clustered.
    public required IReadOnlyList<VisualCluster> Clusters { get; init; }
}
