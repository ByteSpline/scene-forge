using SceneForge.Media.Domain;
using SceneForge.Media.Probing;

namespace SceneForge.Media.Tests.TestSupport;

internal sealed class FakeFfprobeService : IFfprobeService
{
    private readonly Func<string, CancellationToken, Task<MediaInfo>> _handler;

    public FakeFfprobeService(Func<string, CancellationToken, Task<MediaInfo>> handler)
    {
        _handler = handler;
    }

    public static FakeFfprobeService ReturningMediaInfo(MediaInfo mediaInfo) => new((_, _) => Task.FromResult(mediaInfo));

    public static FakeFfprobeService Throwing(Exception exception) => new((_, _) => throw exception);

    // Lets tests assert a caller reused an already-resolved MediaInfo
    // instead of re-probing (e.g. TransitionDetector/CleanClipExtractor's
    // MediaInfo-accepting overloads should never call this at all).
    public int ProbeCallCount { get; private set; }

    public Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        ProbeCallCount++;
        return _handler(filePath, cancellationToken);
    }
}
