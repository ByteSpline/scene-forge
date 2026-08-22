namespace SceneForge.Media.Validation;

public static class OutputDirectoryValidator
{
    // Creates the directory if needed and proves it is actually writable by
    // writing and deleting a probe file, rather than trusting ACL metadata
    // (CLAUDE.md rule 12: outputs must always land on a path the caller can
    // actually write to, never assumed).
    public static string EnsureWritable(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new MediaValidationException(MediaValidationFailureReason.InvalidPath, "An output directory path is required.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(directoryPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new MediaValidationException(MediaValidationFailureReason.InvalidPath, $"'{directoryPath}' is not a valid local directory path.");
        }

        if (File.Exists(fullPath))
        {
            throw new MediaValidationException(MediaValidationFailureReason.PathIsDirectory, $"'{fullPath}' is a file, not a directory.");
        }

        var probeFile = Path.Combine(fullPath, $".sceneforge-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(fullPath);
            File.WriteAllBytes(probeFile, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MediaValidationException(MediaValidationFailureReason.OutputDirectoryNotWritable, $"Output directory '{fullPath}' is not writable: {ex.Message}");
        }
        finally
        {
            File.Delete(probeFile);
        }

        return fullPath;
    }

    // Never overwrite a source input file, even indirectly by resolving to
    // the same path (CLAUDE.md rules 11-12).
    public static void EnsureDoesNotOverwriteInput(string outputFilePath, string inputFilePath)
    {
        var fullOutput = Path.GetFullPath(outputFilePath);
        var fullInput = Path.GetFullPath(inputFilePath);

        if (string.Equals(fullOutput, fullInput, StringComparison.OrdinalIgnoreCase))
        {
            throw new MediaValidationException(MediaValidationFailureReason.OutputWouldOverwriteInput, $"Output path '{fullOutput}' would overwrite the source input file.");
        }
    }
}
