namespace SceneForge.Media.Rendering;

// Which self-correction tier FFmpegRenderService applied after a
// DURATION-ONLY verification miss (every other RenderVerificationResult
// check already passed - see FFmpegRenderService.IsDurationOnlyFailure).
// Ordered the same way the correction loop tries them: cheapest/most-likely
// first, the guaranteed-effective remux last.
public enum RenderDurationCorrectionKind
{
    // Re-ran the exact same render (same encoder, same plan) once more -
    // cheap insurance against transient/non-deterministic encoder timing
    // jitter, since "time is not a concern" but a second identical attempt
    // is otherwise free to try.
    SameEncoderRetry,

    // Re-ran the whole render with a forced software encoder - deterministic,
    // sidesteps hardware-encoder frame-timing drift as the likely cause.
    ForcedSoftwareEncoderRetry,

    // Re-processed the already-assembled output file directly: padded (if
    // short) then frame-domain-trimmed (always) to force the exact planned
    // frame/sample count on both streams - see RenderDurationCorrector.
    // Guaranteed effective by construction, not by hoping a re-encode lines
    // up differently.
    FrameExactRemux,
}

// One self-correction FFmpegRenderService made after a duration-only
// verification miss - recorded on RenderResult.DurationCorrections (and
// written to System.Diagnostics.Trace as it happens) purely for
// transparency/debuggability. A non-empty list means the render still
// succeeded and the corrected output is what the caller received; it is
// never surfaced to the UI as a failure (see
// docs/RENDER_DURATION_SELF_CORRECTION.md).
public sealed record RenderDurationCorrectionEvent
{
    public required RenderDurationCorrectionKind Kind { get; init; }

    // The verification snapshot that triggered this tier - i.e. what was
    // still wrong immediately before this correction was attempted.
    public required TimeSpan ActualDuration { get; init; }

    public required TimeSpan ExpectedDuration { get; init; }

    public required TimeSpan DurationDelta { get; init; }
}
