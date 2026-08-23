namespace SceneForge.Media.Detection;

public sealed record TransitionDetectionProgress
{
    public required int FramesAnalyzed { get; init; }

    // Raw, pre-fusion classifier candidate count observed so far - fast to
    // compute on every frame, but NOT deduplicated/merged (the same real
    // transition is deliberately re-classified many times as the sliding
    // window advances - see ClassifierWindow) and therefore always an
    // overcount relative to the final detection list DetectAsync returns.
    // Useful only as an "is this still finding activity" progress signal,
    // never as a preview of the final count.
    public required int RawCandidatesSoFar { get; init; }

    // Source timestamp of the most recently analyzed frame.
    public required TimeSpan LastSourceTimestamp { get; init; }

    // Wall-clock time since detection started.
    public required TimeSpan Elapsed { get; init; }
}
