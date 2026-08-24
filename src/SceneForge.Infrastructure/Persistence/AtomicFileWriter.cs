namespace SceneForge.Infrastructure.Persistence;

// Every write this persistence layer performs goes through here: write the
// new content to a sibling temporary file, flush it, then atomically replace
// (or, the first time, move) it into place at the real target path. A
// reader can never observe a half-written target file, and a crash between
// the temp-file write and the replace leaves the previous target file (or
// none, on first write) exactly as it was - never a corrupted one.
public static class AtomicFileWriter
{
    public static async Task WriteAsync(
        string targetFilePath,
        Func<Stream, CancellationToken, Task> writeBody,
        ITempFileRegistry? tempFileRegistry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);
        ArgumentNullException.ThrowIfNull(writeBody);

        var fullTargetPath = Path.GetFullPath(targetFilePath);
        var directory = Path.GetDirectoryName(fullTargetPath)
            ?? throw new ArgumentException($"'{targetFilePath}' does not resolve to a valid file path.", nameof(targetFilePath));
        Directory.CreateDirectory(directory);

        var tempFilePath = Path.Combine(directory, $"{Path.GetFileName(fullTargetPath)}.tmp-{Guid.NewGuid():N}");
        tempFileRegistry?.Register(tempFilePath);

        try
        {
            await using (var stream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await writeBody(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(fullTargetPath))
            {
                File.Replace(tempFilePath, fullTargetPath, null);
            }
            else
            {
                File.Move(tempFilePath, fullTargetPath);
            }
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }

            tempFileRegistry?.Unregister(tempFilePath);
        }
    }
}
