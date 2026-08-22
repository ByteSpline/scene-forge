namespace SceneForge.Media.Processes;

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken);
}
