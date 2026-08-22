using SceneForge.Media.Sampling;

namespace SceneForge.Media.Tests.TestSupport;

internal sealed class FakeFrameSamplingProcessLauncher : IFrameSamplingProcessLauncher
{
    private readonly Func<string, IReadOnlyList<string>, FakeFrameSamplingProcess> _factory;

    public FakeFrameSamplingProcessLauncher(Func<string, IReadOnlyList<string>, FakeFrameSamplingProcess> factory)
    {
        _factory = factory;
    }

    public List<(string FfmpegPath, IReadOnlyList<string> Arguments)> Requests { get; } = [];

    public FakeFrameSamplingProcess? LastProcess { get; private set; }

    public Task<IFrameSamplingProcess> StartAsync(string ffmpegPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Requests.Add((ffmpegPath, arguments));
        var process = _factory(ffmpegPath, arguments);
        LastProcess = process;
        return Task.FromResult<IFrameSamplingProcess>(process);
    }
}
