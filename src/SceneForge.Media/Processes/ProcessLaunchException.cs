namespace SceneForge.Media.Processes;

public sealed class ProcessLaunchException : Exception
{
    public string FileName { get; }

    public ProcessLaunchException(string fileName, string message, Exception innerException)
        : base(message, innerException)
    {
        FileName = fileName;
    }
}
