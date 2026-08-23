using System.Runtime.CompilerServices;
using SceneForge.Media.Domain;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Tests.TestSupport;

internal sealed class FakeFrameSampler : IFrameSampler
{
    private readonly Func<string, FrameSamplingOptions, CancellationToken, IAsyncEnumerable<FrameSample>> _handler;

    public FakeFrameSampler(Func<string, FrameSamplingOptions, CancellationToken, IAsyncEnumerable<FrameSample>> handler)
    {
        _handler = handler;
    }

    public static FakeFrameSampler ReturningFrames(Func<IEnumerable<FrameSample>> factory) =>
        new((_, _, cancellationToken) => ToAsyncEnumerable(factory(), cancellationToken));

    public IAsyncEnumerable<FrameSample> SampleAsync(
        string filePath,
        FrameSamplingOptions options,
        IProgress<FrameSamplingProgress>? progress,
        CancellationToken cancellationToken) => _handler(filePath, options, cancellationToken);

    // mediaInfo is ignored here - fakes drive behavior purely off the
    // handler delegate; tests that care about MediaInfo do so via
    // FakeFfprobeService instead.
    public IAsyncEnumerable<FrameSample> SampleAsync(
        string filePath,
        MediaInfo mediaInfo,
        FrameSamplingOptions options,
        IProgress<FrameSamplingProgress>? progress,
        CancellationToken cancellationToken) => _handler(filePath, options, cancellationToken);

    private static async IAsyncEnumerable<FrameSample> ToAsyncEnumerable(
        IEnumerable<FrameSample> frames,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return frame;
        }

        await Task.CompletedTask;
    }
}
