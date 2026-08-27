using SceneForge.Media.Tooling;

namespace SceneForge.Media.Tests.TestSupport;

internal sealed class FakeFfmpegToolLocator : IFfmpegToolLocator
{
    private readonly FfmpegToolPaths _paths;
    private readonly Exception? _exceptionToThrow;

    public FakeFfmpegToolLocator(FfmpegToolPaths? paths = null, Exception? exceptionToThrow = null)
    {
        _paths = paths ?? new FfmpegToolPaths { FfprobePath = "ffprobe.exe", FfmpegPath = "ffmpeg.exe" };
        _exceptionToThrow = exceptionToThrow;
    }

    public Task<FfmpegToolPaths> LocateAsync(CancellationToken cancellationToken) =>
        _exceptionToThrow is null ? Task.FromResult(_paths) : throw _exceptionToThrow;
}
