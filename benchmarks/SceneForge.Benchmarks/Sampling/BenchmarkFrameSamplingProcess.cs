using SceneForge.Media.Sampling;

namespace SceneForge.Benchmarks.Sampling;

internal sealed class BenchmarkFrameSamplingProcess : IFrameSamplingProcess
{
    public BenchmarkFrameSamplingProcess(Stream standardOutput, TextReader standardError)
    {
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public Stream StandardOutput { get; }

    public TextReader StandardError { get; }

    public int ExitCode => 0;

    public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Kill()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class BenchmarkFrameSamplingProcessLauncher : IFrameSamplingProcessLauncher
{
    private readonly Func<IFrameSamplingProcess> _factory;

    public BenchmarkFrameSamplingProcessLauncher(Func<IFrameSamplingProcess> factory)
    {
        _factory = factory;
    }

    public Task<IFrameSamplingProcess> StartAsync(string ffmpegPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        Task.FromResult(_factory());
}
