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

    public Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken) => _handler(filePath, cancellationToken);
}
