namespace SceneForge.Media.Sampling;

public sealed class FrameSamplingException : Exception
{
    public FrameSamplingException(string message)
        : base(message)
    {
    }

    public FrameSamplingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
