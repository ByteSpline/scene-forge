using SceneForge.Media.Domain;

namespace SceneForge.Media.Extraction;

// Where an excluded interval's evidence came from - kept explicit (rather
// than collapsing everything into one opaque "excluded" bucket) so
// diagnostics/JSON export can explain *why* a portion of a scene was
// subtracted, and so CleanClipExtractor never has to guess intent.
public enum ExclusionKind
{
    // A buffered transition interval (TransitionDetection.Start..End,
    // already including TransitionDetectionProfile's pre/post safety
    // buffers) - see TransitionDetection's remarks: this is a contaminated
    // interval, never clean footage of either neighboring scene.
    Transition,

    // A black, frozen, or otherwise invalid/undecodable interval, as
    // determined by whatever upstream validation produced it. Not computed
    // by CleanClipExtractor itself - taken as a caller-supplied fact, same
    // as Transition.
    BlackFrozenInvalid,

    // An interval a user explicitly marked as excluded (e.g. via the UI).
    UserExcluded,
}

// One interval to subtract from scene ranges before any clip candidate is
// ever generated. CleanClipExtractor never attempts to reconstruct or
// otherwise use frames inside an excluded interval - see IntervalSubtractor.
public sealed record ExcludedInterval
{
    public required TimeRange Range { get; init; }

    public required ExclusionKind Kind { get; init; }

    // Optional human-readable detail (e.g. "FadeToBlack" or "user-marked at
    // 00:12"), carried through to JSON export for manual inspection - never
    // required, since not every exclusion source has one.
    public string? Reason { get; init; }
}
