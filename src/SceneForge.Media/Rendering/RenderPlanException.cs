namespace SceneForge.Media.Rendering;

public sealed class RenderPlanException : Exception
{
    public RenderPlanException(string message)
        : base(message)
    {
    }

    public RenderPlanException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
