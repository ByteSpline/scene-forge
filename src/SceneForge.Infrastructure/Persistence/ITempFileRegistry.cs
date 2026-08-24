namespace SceneForge.Infrastructure.Persistence;

// Tracks every temporary file this application creates so it can always be
// cleaned up - on normal completion, and (via SweepOrphansAsync) at the next
// startup after a crash left one behind. Every method is scoped to exactly
// one app-owned root directory (RootDirectory): registering or deleting a
// path outside that root is refused/skipped rather than performed
// (CLAUDE.md rule 11 - never delete a file outside the app's own temp area,
// and never touch a user's source/output files).
public interface ITempFileRegistry
{
    string RootDirectory { get; }

    IReadOnlyList<string> RegisteredFiles { get; }

    // Throws InvalidOperationException if filePath does not resolve under
    // RootDirectory.
    void Register(string filePath);

    void Unregister(string filePath);

    // Deletes every currently-registered file that still exists and clears
    // the registry - the "on normal completion" cleanup path.
    Task CleanupAsync(CancellationToken cancellationToken = default);

    // Deletes any file directly under RootDirectory that is not currently
    // registered - the "leftover from a process that died before it could
    // clean up after itself" path, run once at startup.
    Task SweepOrphansAsync(CancellationToken cancellationToken = default);
}
