namespace SceneForge.Media.Rendering;

public interface IHardwareEncoderProbe
{
    // Tries each candidate encoder in priority order (NVENC, Quick Sync,
    // AMF, then libx264) by actually launching a short real encode against
    // synthetic input and checking the process exits cleanly - see
    // HardwareEncoderProbe. Throws RenderExecutionException only if every
    // candidate, including the always-expected-to-work libx264 software
    // fallback, fails.
    Task<VideoEncoderSelection> SelectEncoderAsync(CancellationToken cancellationToken);
}
