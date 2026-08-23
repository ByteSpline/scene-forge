namespace SceneForge.Media.Rendering.Internal;

// A plain synchronous IProgress<T> adapter - unlike System.Progress<T>,
// which captures SynchronizationContext.Current at construction and
// otherwise posts every Report through the ThreadPool, deferring delivery
// with no ordering/timing guarantee relative to the caller. FFmpegRenderService
// uses this only as an internal relay from ProcessRunner's own synchronous
// per-line callback (see ProcessRunner.PumpAsync, which calls
// progress?.Report(...) directly inline) through to RenderProgressParser -
// an already-async pipe, so the extra queuing/deferral System.Progress<T>
// would add serves no purpose and would silently make ffmpeg's reported
// progress lag or reorder relative to when it was actually emitted.
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler)
    {
        _handler = handler;
    }

    public void Report(T value) => _handler(value);
}
