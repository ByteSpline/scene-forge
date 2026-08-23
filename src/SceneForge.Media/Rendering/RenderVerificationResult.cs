namespace SceneForge.Media.Rendering;

// What RenderOutputVerifier actually checked against the rendered file via
// ffprobe/ffmpeg - never a bare pass/fail, so a caller (or
// RenderVerificationException's message) can always state exactly which
// expectation failed (CLAUDE.md rule 10: never claim correctness opaquely).
public sealed record RenderVerificationResult
{
    public required bool HasExpectedVideoStream { get; init; }

    // Exactly one audio stream is expected: the single supplied audio
    // track, and nothing from the source.
    public required bool HasExactlyOneAudioStream { get; init; }

    public required TimeSpan ExpectedDuration { get; init; }

    public required TimeSpan ActualDuration { get; init; }

    // ActualDuration - ExpectedDuration - always reported, never hidden
    // inside a pass/fail bit (same "never claim exactness without stating
    // what it was measured against" convention TimelinePlan.AudioDurationRoundingError
    // established in Phase 8).
    public required TimeSpan DurationDelta { get; init; }

    // One frame at RenderOutputSpec.FrameRate - the phase brief's literal
    // "duration tolerance of one frame", never a looser tolerance.
    public required TimeSpan DurationTolerance { get; init; }

    public required bool DurationWithinTolerance { get; init; }

    public required bool FirstFrameDecodable { get; init; }

    public required bool MiddleFrameDecodable { get; init; }

    public required bool LastFrameDecodable { get; init; }

    public bool IsValid =>
        HasExpectedVideoStream
        && HasExactlyOneAudioStream
        && DurationWithinTolerance
        && FirstFrameDecodable
        && MiddleFrameDecodable
        && LastFrameDecodable;

    // Human-readable list of every check that failed - empty when IsValid.
    public IReadOnlyList<string> Failures => BuildFailures();

    private List<string> BuildFailures()
    {
        var failures = new List<string>();
        if (!HasExpectedVideoStream)
        {
            failures.Add("output has no decodable video stream");
        }

        if (!HasExactlyOneAudioStream)
        {
            failures.Add("output does not contain exactly one audio stream");
        }

        if (!DurationWithinTolerance)
        {
            failures.Add($"duration {ActualDuration} differs from expected {ExpectedDuration} by {DurationDelta} (tolerance {DurationTolerance})");
        }

        if (!FirstFrameDecodable)
        {
            failures.Add("first frame is not decodable");
        }

        if (!MiddleFrameDecodable)
        {
            failures.Add("middle frame is not decodable");
        }

        if (!LastFrameDecodable)
        {
            failures.Add("last frame is not decodable");
        }

        return failures;
    }
}
