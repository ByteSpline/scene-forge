namespace SceneForge.Infrastructure.Persistence;

// Every write this persistence layer performs goes through here: write the
// new content to a sibling temporary file, flush it, then atomically replace
// (or, the first time, move) it into place at the real target path. A
// reader can never observe a half-written target file, and a crash between
// the temp-file write and the replace leaves the previous target file (or
// none, on first write) exactly as it was - never a corrupted one.
//
// This sibling temp file deliberately has nothing to do with
// ITempFileRegistry. The registry tracks scratch files the app creates
// directly under its own app-owned Temp root (see TempFileRegistry's
// remarks) so it can enumerate and sweep exactly that one directory; this
// writer's temp file instead lives beside whatever target path the caller
// passes in - e.g. a project checkpoint under .../Projects/<id>/ - which is
// never inside, and must never be checked against, that Temp-root allowlist.
// (Feeding this writer's temp file into the registry used to throw
// InvalidOperationException on every single project save, since
// Projects/<id>/ and Temp/ are permanent sibling directories under the same
// app data root - see ProjectStoreTests's regression test for the exact
// failure this caused.) Crash safety here comes entirely from the
// write-then-replace pattern itself: a leftover ".tmp-<guid>" file next to a
// target is inert, since only the real target path is ever read back.
public static class AtomicFileWriter
{
    public static async Task WriteAsync(
        string targetFilePath,
        Func<Stream, CancellationToken, Task> writeBody,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);
        ArgumentNullException.ThrowIfNull(writeBody);

        var fullTargetPath = Path.GetFullPath(targetFilePath);
        var directory = Path.GetDirectoryName(fullTargetPath)
            ?? throw new ArgumentException($"'{targetFilePath}' does not resolve to a valid file path.", nameof(targetFilePath));
        Directory.CreateDirectory(directory);

        var tempFilePath = Path.Combine(directory, $"{Path.GetFileName(fullTargetPath)}.tmp-{Guid.NewGuid():N}");

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
        }
    }
}
