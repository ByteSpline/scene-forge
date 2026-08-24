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
    // registered AND has not been modified within the last
    // TempFileRegistry.DefaultMinimumOrphanAge - the "leftover from a
    // process that died before it could clean up after itself" path, run
    // once at startup. The age guard exists because nothing in
    // SceneForge.App enforces single-instance operation: without it, a
    // second concurrently-running instance's own in-flight, not-yet-
    // registered-in-THIS-instance's-manifest temp file could otherwise be
    // deleted out from under it.
    Task SweepOrphansAsync(CancellationToken cancellationToken = default);

    // Same sweep, with an explicit age threshold instead of the default
    // (exposed primarily so tests can exercise both sides of the boundary
    // without waiting for real wall-clock time to pass).
    Task SweepOrphansAsync(TimeSpan minimumOrphanAge, CancellationToken cancellationToken = default);
}
