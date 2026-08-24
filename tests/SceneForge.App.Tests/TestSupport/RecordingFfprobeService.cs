using SceneForge.Media.Domain;
using SceneForge.Media.Probing;

namespace SceneForge.App.Tests.TestSupport;

// Records every CancellationToken it was actually called with (so a test
// can assert a caller passed a real, cancellable token rather than
// CancellationToken.None) and can optionally hang until the caller's token
// is cancelled, to deterministically exercise a timeout/cancellation path
// without waiting on a real wall-clock delay.
internal sealed class RecordingFfprobeService : IFfprobeService
{
    public List<CancellationToken> TokensReceived { get; } = [];

    public MediaInfo? ResultToReturn { get; set; }

    public bool HangUntilCancelled { get; set; }

    public async Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        TokensReceived.Add(cancellationToken);

        if (HangUntilCancelled)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return ResultToReturn ?? throw new InvalidOperationException("No fake probe result configured.");
    }
}
