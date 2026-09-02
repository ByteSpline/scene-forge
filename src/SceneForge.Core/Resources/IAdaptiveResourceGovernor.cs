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
    // The app-wide CPU-equivalent budget: roughly 35% of this machine's
    // logical processors (Max(1, floor(Environment.ProcessorCount * 0.35)) -
    // see AdaptiveResourceGovernor.CpuBudgetFraction), never zero even on a
    // single-core machine. This is a hard ceiling, not "leave one core
    // free": every ffmpeg invocation in the app must pass this value (or a
    // documented share of it) as its -threads argument, and every OpenCV
    // call path must cap Cv2.SetNumThreads to it (or a documented share) -
    // MaxWorkers on its own only bounds how many *processes/tasks* run
    // concurrently, not how many threads each one uses internally, and
    // ffmpeg/OpenCV both default to using every logical CPU on their own if
    // not told otherwise. See FrameSampler and FFmpegRenderService for the
    // two composition points that apply this.
    int MaxWorkers { get; }

    // Throws InsufficientDiskSpaceException if the drive containing path
    // has less than requiredBytes free. A synchronous check (DriveInfo's
    // free-space read is a fast, non-blocking syscall, not a long-running
    // operation CLAUDE.md rule 5 would require cancellation for) - callers
    // run this once, up front, before starting a write, not as a progress
    // poll.
    void EnsureSufficientDiskSpace(string path, long requiredBytes);
}
