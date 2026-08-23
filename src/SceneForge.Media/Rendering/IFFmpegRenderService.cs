namespace SceneForge.Media.Rendering;

public interface IFFmpegRenderService
{
    // Renders plan to outputFilePath (a caller-selected new path -
    // CLAUDE.md rule 12: never the source file, never silently overwritten)
    // in a single ffmpeg pass wherever possible, reporting machine-readable
    // progress via progress, and verifying the result via ffprobe/decode
    // checks before returning - see RenderResult/RenderVerificationResult.
    // Throws RenderExecutionException if ffmpeg itself fails (including
    // after a hardware-encoder fallback attempt) and RenderVerificationException
    // if the rendered file fails post-render verification.
    Task<RenderResult> RenderAsync(
        RenderPlan plan,
        string outputFilePath,
        IProgress<RenderProgress>? progress,
        CancellationToken cancellationToken = default);
}
