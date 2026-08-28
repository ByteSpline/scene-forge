namespace SceneForge.Media.Rendering;

public interface IHardwareEncoderProbe
{
    // Tries each candidate encoder in priority order (NVENC, Quick Sync,
    // AMF, then the software encoders libx264 / libopenh264) by actually
    // launching a short real encode against synthetic input and checking the
    // process exits cleanly - see HardwareEncoderProbe. Throws
    // RenderExecutionException only if every candidate, including the
    // software fallbacks, fails.
    Task<VideoEncoderSelection> SelectEncoderAsync(CancellationToken cancellationToken);

    // The best *software* encoder alone, skipping every hardware candidate -
    // what FFmpegRenderService retries with when a hardware render fails
    // mid-way. Kept separate from SelectEncoderAsync (which prefers hardware)
    // so the fallback name is never hardcoded: an ffmpeg build compiled
    // --disable-libx264 (SceneForge's own vendored build is one) has no
    // libx264 at all, and the fallback must resolve to libopenh264 there
    // rather than failing the whole render. Throws RenderExecutionException
    // if no software encoder works.
    Task<VideoEncoderSelection> SelectSoftwareEncoderAsync(CancellationToken cancellationToken);
}
