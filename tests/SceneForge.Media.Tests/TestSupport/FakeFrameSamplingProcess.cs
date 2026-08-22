using SceneForge.Media.Sampling;

namespace SceneForge.Media.Tests.TestSupport;

internal sealed class FakeFrameSamplingProcess : IFrameSamplingProcess
{
    public FakeFrameSamplingProcess(Stream standardOutput, TextReader standardError, int exitCode = 0)
    {
        StandardOutput = standardOutput;
        StandardError = standardError;
        ExitCode = exitCode;
    }

    public Stream StandardOutput { get; }

    public TextReader StandardError { get; }

    public int ExitCode { get; }

    public bool KillRequested { get; private set; }

    public int KillCount { get; private set; }

    public bool Disposed { get; private set; }

    public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Kill()
    {
        KillRequested = true;
        KillCount++;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
