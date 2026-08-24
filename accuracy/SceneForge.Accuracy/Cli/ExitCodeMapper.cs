namespace SceneForge.Accuracy.Cli;

// Isolated from CommandDispatcher.RunAsync so this mapping is directly
// unit-testable without needing a real command (and therefore real
// ffmpeg) to actually throw. Conventional SIGINT exit code (130) for
// cancellation - matches this being triggered by the same Ctrl+C path a
// shell user already expects that code from.
public static class ExitCodeMapper
{
    public const int CancelledExitCode = 130;
    public const int FailureExitCode = 1;

    public static int Map(Exception exception, string command, TextWriter errorWriter)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(errorWriter);

        switch (exception)
        {
            case OperationCanceledException:
                errorWriter.WriteLine("Cancelled.");
                return CancelledExitCode;

            case ArgumentException argumentException:
                errorWriter.WriteLine($"'{command}': {argumentException.Message}");
                return FailureExitCode;

            default:
                errorWriter.WriteLine($"'{command}' failed: {exception.Message}");
                return FailureExitCode;
        }
    }
}
