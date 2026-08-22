using SceneForge.Media.Domain;
using SceneForge.Media.Probing;
using SceneForge.Media.Tooling;

namespace SceneForge.Benchmarks.Sampling;

internal sealed class BenchmarkFfprobeService : IFfprobeService
{
    private readonly MediaInfo _mediaInfo;

    public BenchmarkFfprobeService(MediaInfo mediaInfo)
    {
        _mediaInfo = mediaInfo;
    }

    public Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken) => Task.FromResult(_mediaInfo);
}

// Never actually invoked (the process launcher is faked too), but
// FrameSampler's public constructor requires a real IFfmpegToolLocator.
internal sealed class BenchmarkFfmpegToolLocator : IFfmpegToolLocator
{
    public Task<FfmpegToolPaths> LocateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new FfmpegToolPaths { FfprobePath = "ffprobe.exe", FfmpegPath = "ffmpeg.exe" });
}
