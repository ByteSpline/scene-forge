using SceneForge.Media.Domain;

namespace SceneForge.Media.Extraction;

// One 3-5 second candidate clip drawn from a scene range's remaining
// (post-subtraction) footage, scored and perceptually fingerprinted.
// Produced whether or not it was ultimately accepted - see
// CleanClipExtractionResult, which keeps both AcceptedClips and
// RejectedClips (CLAUDE.md rule 10/explainability: rejection is never
// silent).
public sealed record CleanClip
{
    public required TimeRange Range { get; init; }

    // Index into the caller's original SceneRanges list this candidate was
    // generated from - lets a caller trace a clip back to its source scene
    // even after subtraction/candidate generation has reshaped the
    // timeline.
    public required int SourceSceneIndex { get; init; }

    public required ClipScore Score { get; init; }

    public required PerceptualDescriptor Descriptor { get; init; }

    // Set once VisualClusterer has grouped the accepted clips; null for
    // rejected clips, which are never clustered.
    public int? ClusterId { get; init; }
}
