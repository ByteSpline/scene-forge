namespace SceneForge.Media.Rendering;

// What one successful FFmpegRenderService.RenderAsync call produced.
// Verification is always present and always passed (IsValid) by the time
// this is returned - a failing verification throws RenderVerificationException
// instead (see RenderVerificationException), so a caller never has to
// remember to check Verification.IsValid itself.
public sealed record RenderResult
{
    public required string OutputFilePath { get; init; }

    public required VideoEncoderSelection Encoder { get; init; }

    // True when the initially selected hardware encoder's actual render
    // attempt failed (distinct from HardwareEncoderProbe's own smoke test
    // passing) and FFmpegRenderService retried once with libx264 - see
    // FFmpegRenderService's "Hardware output must be validated and fall
    // back safely" handling.
    public required bool FellBackToSoftwareEncoder { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required RenderVerificationResult Verification { get; init; }

    // Empty for a single-pass render and for a batched render that never hit
    // a memory limit. Otherwise one entry per batch that ffmpeg failed to
    // allocate and FFmpegRenderService automatically re-rendered as two
    // smaller batches - see RenderBatchSplitEvent. A non-empty list means
    // the render still succeeded; it is a record of the adaptive sizing the
    // service did to get there, not a failure.
    public IReadOnlyList<RenderBatchSplitEvent> BatchSplitEvents { get; init; } = [];

    // Empty unless the output initially missed RenderOutputVerifier's
    // duration tolerance and FFmpegRenderService had to self-correct - see
    // RenderDurationCorrectionEvent. As with BatchSplitEvents, a non-empty
    // list means the render still succeeded; it is a record of what the
    // service did internally to get there, never a failure the caller needs
    // to react to.
    public IReadOnlyList<RenderDurationCorrectionEvent> DurationCorrections { get; init; } = [];
}
