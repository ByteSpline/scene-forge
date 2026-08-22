namespace SceneForge.Media.Processes;

public sealed record ProcessExecutionResult
{
    public required int ExitCode { get; init; }

    // Bounded to MaxCapturedBytesPerStream; may end with a truncation marker.
    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public required TimeSpan Elapsed { get; init; }
}
