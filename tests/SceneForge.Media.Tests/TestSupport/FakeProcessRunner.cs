using SceneForge.Media.Processes;

namespace SceneForge.Media.Tests.TestSupport;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<ProcessExecutionRequest, CancellationToken, Task<ProcessExecutionResult>> _handler;

    public List<ProcessExecutionRequest> Requests { get; } = [];

    public FakeProcessRunner(Func<ProcessExecutionRequest, CancellationToken, Task<ProcessExecutionResult>> handler)
    {
        _handler = handler;
    }

    public static FakeProcessRunner ReturningResult(ProcessExecutionResult result) => new((_, _) => Task.FromResult(result));

    public static FakeProcessRunner Throwing(Exception exception) => new((_, _) => throw exception);

    public Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return _handler(request, cancellationToken);
    }
}
