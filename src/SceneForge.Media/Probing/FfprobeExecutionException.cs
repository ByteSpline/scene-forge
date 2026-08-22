namespace SceneForge.Media.Probing;

public sealed class FfprobeExecutionException : Exception
{
    public int? ExitCode { get; }

    public string? StandardError { get; }

    public FfprobeExecutionException(int exitCode, string standardError)
        : base($"ffprobe exited with code {exitCode}: {standardError}")
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }

    public FfprobeExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
        ExitCode = null;
        StandardError = null;
    }

    public FfprobeExecutionException(string message)
        : base(message)
    {
        ExitCode = null;
        StandardError = null;
    }
}
