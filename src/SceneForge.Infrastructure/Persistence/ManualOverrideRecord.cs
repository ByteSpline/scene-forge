namespace SceneForge.Infrastructure.Persistence;

// The persisted analogue of SceneForge.App.Session.ClipReviewOverride - one
// user edit (include/exclude, boundary adjustment) to a candidate clip from
// Scene Review, keyed the same way the in-memory session keys it (a clip's
// stable position across AcceptedClips followed by RejectedClips).
public sealed record ManualOverrideRecord
{
    public required int ClipIndex { get; init; }

    public required bool IsIncluded { get; init; }

    public required TimeSpan AdjustedStart { get; init; }

    public required TimeSpan AdjustedEnd { get; init; }
}
