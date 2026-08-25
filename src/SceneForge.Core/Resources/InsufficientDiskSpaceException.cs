namespace SceneForge.Core.Resources;

// Derives from IOException deliberately: every UI-facing catch site that
// already treats a bare IOException as a recognized, user-visible failure
// (AnalysisProgressViewModel, RenderProgressViewModel) picks this up for
// free, with no new catch clause needed.
public sealed class InsufficientDiskSpaceException : IOException
{
    public string Path { get; }

    public long RequiredBytes { get; }

    public long AvailableBytes { get; }

    public InsufficientDiskSpaceException(string path, long requiredBytes, long availableBytes)
        : base($"Insufficient disk space at '{path}': {requiredBytes:N0} bytes required, only {availableBytes:N0} bytes available.")
    {
        Path = path;
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }
}
