using SceneForge.Media.Extraction;

namespace SceneForge.Infrastructure.Persistence;

// A lean, persisted projection of one CleanClip - just enough to redisplay
// Scene Review's list and audit past decisions without re-running
// extraction. Deliberately drops CleanClip.Descriptor (PerceptualDescriptor's
// hash/histograms): that data only ever serves VisualClusterer during a
// single extraction run and is cheap to recompute from the source video, so
// persisting it would grow every project file for no resume-time benefit
// (see docs/PHASE_11_REPORT.md, Design summary).
public sealed record ClipMetadataRecord
{
    // Stable position across AcceptedClips followed by RejectedClips - the
    // same index ManualOverrideRecord.ClipIndex refers to.
    public required int Index { get; init; }

    public required bool IsAccepted { get; init; }

    public required TimeSpan RangeStart { get; init; }

    public required TimeSpan RangeEnd { get; init; }

    public required int SourceSceneIndex { get; init; }

    public required ClipScore Score { get; init; }

    public int? ClusterId { get; init; }
}
