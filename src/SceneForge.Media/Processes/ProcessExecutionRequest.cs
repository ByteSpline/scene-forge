namespace SceneForge.Media.Processes;

public sealed record ProcessExecutionRequest
{
    public required string FileName { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public TimeSpan? Timeout { get; init; }

    public IProgress<ProcessOutputLine>? OutputProgress { get; init; }

    // Hard upper bound regardless of what a caller requests, so a
    // misconfigured caller can never turn this into an unbounded buffer.
    private const int AbsoluteMaxCapturedBytes = 64 * 1024 * 1024;

    private readonly int _maxCapturedBytesPerStream = 4 * 1024 * 1024;

    public int MaxCapturedBytesPerStream
    {
        get => _maxCapturedBytesPerStream;
        init => _maxCapturedBytesPerStream = Math.Clamp(value, 1024, AbsoluteMaxCapturedBytes);
    }
}
