namespace SceneForge.Core.Resources;

// Central point for the two adaptive-resource-control policies CLAUDE.md
// rule 6 requires but no single call site owns on its own: how many workers
// a bounded pool should run at once, and whether a write is about to run a
// disk out of space. Lives in SceneForge.Core (the one project every other
// project already depends on - see the ProjectReference comments in
// SceneForge.Infrastructure.csproj/SceneForge.Media.csproj) specifically so
// both SceneForge.Media (rendering) and SceneForge.App (thumbnail
// generation) can consume it without SceneForge.Infrastructure needing a
// reverse reference from Media, which would create a cycle.
public interface IAdaptiveResourceGovernor
{
    // Max(1, Environment.ProcessorCount - 1) - leaves one logical CPU free
    // for the OS/UI/other applications rather than saturating every core.
    // Never zero, even on a single-core machine.
    int MaxWorkers { get; }

    // Throws InsufficientDiskSpaceException if the drive containing path
    // has less than requiredBytes free. A synchronous check (DriveInfo's
    // free-space read is a fast, non-blocking syscall, not a long-running
    // operation CLAUDE.md rule 5 would require cancellation for) - callers
    // run this once, up front, before starting a write, not as a progress
    // poll.
    void EnsureSufficientDiskSpace(string path, long requiredBytes);
}
