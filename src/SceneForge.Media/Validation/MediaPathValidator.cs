namespace SceneForge.Media.Validation;

public static class MediaPathValidator
{
    public static string ValidateInputFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new MediaValidationException(MediaValidationFailureReason.InvalidPath, "A media file path is required.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new MediaValidationException(MediaValidationFailureReason.InvalidPath, $"'{path}' is not a valid local file path.");
        }

        if (Directory.Exists(fullPath))
        {
            throw new MediaValidationException(MediaValidationFailureReason.PathIsDirectory, $"'{fullPath}' is a directory, not a media file.");
        }

        if (!File.Exists(fullPath))
        {
            throw new MediaValidationException(MediaValidationFailureReason.FileNotFound, $"Media file '{fullPath}' does not exist.");
        }

        return fullPath;
    }
}
