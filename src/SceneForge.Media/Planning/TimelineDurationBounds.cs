namespace SceneForge.Media.Planning;

// Soft preferences bounding how TimelinePlanner trims the plan's final
// clip - NOT hard requirements. CLAUDE.md's exact-audio-duration-match
// requirement always wins over both of these; when honoring it forces a
// bound to be exceeded, TimelinePlanner still proceeds but records a
// RelaxedConstraint entry in DecisionTrace rather than silently ignoring
// the bound (CLAUDE.md rule 10: nothing about the plan is opaque).
public sealed record TimelineDurationBounds
{
    private readonly TimeSpan _minFinalClipDuration = TimeSpan.Zero;
    private readonly TimeSpan _maxOvershoot = TimeSpan.MaxValue;

    // Preferred floor for the trimmed final clip's used duration. Since the
    // trim amount is dictated entirely by how much of TargetAudioDuration
    // remains when the final placement is made (never by which candidate is
    // chosen - every candidate would be trimmed to the same remaining
    // duration), this cannot be satisfied by picking a different clip; it
    // exists purely so a near-zero-length final trim is flagged rather than
    // silent.
    public TimeSpan MinFinalClipDuration
    {
        get => _minFinalClipDuration;
        init => _minFinalClipDuration = value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    // Preferred ceiling on how far a candidate's full (untrimmed) duration
    // may exceed the remaining budget before being accepted as the final
    // placement. TimelinePlanner always chooses the smallest eligible
    // candidate that still covers the remaining budget (minimizing
    // overshoot by construction), so this bound is only ever exceeded when
    // every eligible candidate is longer than it allows.
    public TimeSpan MaxOvershoot
    {
        get => _maxOvershoot;
        init => _maxOvershoot = value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    public static TimelineDurationBounds Default { get; } = new();
}
