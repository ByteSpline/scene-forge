namespace SceneForge.Media.Tooling;

public interface IFfmpegToolLocator
{
    // Throws FfmpegToolsNotFoundException or FfmpegToolsIncompatibleException
    // rather than returning a partial/null result, so a missing or broken
    // toolchain always surfaces as a clear startup error instead of a silent
    // fallback discovered later during a probe.
    Task<FfmpegToolPaths> LocateAsync(CancellationToken cancellationToken);
}
