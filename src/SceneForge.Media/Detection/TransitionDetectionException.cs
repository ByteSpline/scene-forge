namespace SceneForge.Media.Detection;

public sealed class TransitionDetectionException : Exception
{
    public TransitionDetectionException(string message)
        : base(message)
    {
    }

    public TransitionDetectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
