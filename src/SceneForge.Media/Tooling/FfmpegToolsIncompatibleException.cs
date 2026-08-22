namespace SceneForge.Media.Tooling;

public sealed class FfmpegToolsIncompatibleException : Exception
{
    public string ExecutablePath { get; }

    public FfmpegToolsIncompatibleException(string executablePath, string reason)
        : base($"'{executablePath}' does not look like a compatible FFmpeg tool: {reason}")
    {
        ExecutablePath = executablePath;
    }
}
