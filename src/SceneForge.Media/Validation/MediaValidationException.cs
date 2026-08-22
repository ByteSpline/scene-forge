namespace SceneForge.Media.Validation;

public sealed class MediaValidationException : Exception
{
    public MediaValidationFailureReason Reason { get; }

    public MediaValidationException(MediaValidationFailureReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }
}
