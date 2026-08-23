using SceneForge.Media.Domain;

namespace SceneForge.Media.Planning;

// One clip placed into the planned timeline. ClipIndex is the index into
// the TimelinePlanRequest.AvailableClips this placement was drawn from - a
// caller wanting the full CleanClip (Score, Descriptor, etc.) looks it up
// there rather than TimelinePlacement duplicating it, the same
// SourceSceneIndex-as-back-reference pattern CleanClip itself uses for
// CleanClipExtractionOptions.SceneRanges.
public sealed record TimelinePlacement
{
    // 0-based position in the plan - Placements is always sorted by this.
    public required int Position { get; init; }

    public required int ClipIndex { get; init; }

    // The source clip's own Range, unmodified - always [Start, Start + SourceRange.Duration].
    public required TimeRange SourceRange { get; init; }

    // Portion of SourceRange actually used, measured from SourceRange.Start.
    // Equal to SourceRange.Duration unless IsTrimmed is true, in which case
    // this is strictly shorter - only the last placement in a plan is ever
    // trimmed.
    public required TimeSpan UsedDuration { get; init; }

    public required bool IsTrimmed { get; init; }

    public required int SourceSceneIndex { get; init; }

    public required int? ClusterId { get; init; }

    // Which use of this clip this is (1 for its first placement in the
    // plan, 2 for its second, ...) - never exceeds
    // TimelinePlanRequest.MaximumReuseCount.
    public required int UsageOrdinal { get; init; }
}
