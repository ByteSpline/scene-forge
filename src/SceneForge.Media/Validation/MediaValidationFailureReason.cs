namespace SceneForge.Media.Validation;

public enum MediaValidationFailureReason
{
    InvalidPath,
    PathIsDirectory,
    FileNotFound,
    NoMediaStreams,
    UnknownDuration,
    OutputDirectoryNotWritable,
    OutputWouldOverwriteInput,
}
