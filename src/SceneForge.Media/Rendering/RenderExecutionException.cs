namespace SceneForge.Media.Rendering;

public sealed class RenderExecutionException : Exception
{
    public RenderExecutionException(string message)
        : base(message)
    {
    }

    public RenderExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
