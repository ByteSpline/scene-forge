using SceneForge.Media.Tooling;

namespace SceneForge.Media.Tests.TestSupport;

internal sealed class FakeFfmpegToolLocator : IFfmpegToolLocator
{
    private readonly FfmpegToolPaths _paths;

    public FakeFfmpegToolLocator(FfmpegToolPaths? paths = null)
    {
        _paths = paths ?? new FfmpegToolPaths { FfprobePath = "ffprobe.exe", FfmpegPath = "ffmpeg.exe" };
    }

    public Task<FfmpegToolPaths> LocateAsync(CancellationToken cancellationToken) => Task.FromResult(_paths);
}
