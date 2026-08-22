namespace SceneForge.Media.Processes;

// Extends OperationCanceledException so callers that already catch cancellation
// generically (per CLAUDE.md's cooperative-shutdown rule) also catch timeouts,
// while still being able to branch on the extra diagnostics below if they want to.
public sealed class ProcessTimeoutException : OperationCanceledException
{
    public TimeSpan Timeout { get; }

    public string PartialStandardOutput { get; }

    public string PartialStandardError { get; }

    public ProcessTimeoutException(TimeSpan timeout, string partialStandardOutput, string partialStandardError)
        : base($"Process did not exit within the configured timeout of {timeout}.")
    {
        Timeout = timeout;
        PartialStandardOutput = partialStandardOutput;
        PartialStandardError = partialStandardError;
    }
}
