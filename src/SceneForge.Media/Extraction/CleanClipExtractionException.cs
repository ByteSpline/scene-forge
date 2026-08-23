namespace SceneForge.Media.Extraction;

public sealed class CleanClipExtractionException : Exception
{
    public CleanClipExtractionException(string message)
        : base(message)
    {
    }

    public CleanClipExtractionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
