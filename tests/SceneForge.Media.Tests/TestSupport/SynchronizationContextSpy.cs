namespace SceneForge.Media.Tests.TestSupport;

// Records every Post/Send call routed through it - the two ways a captured
// SynchronizationContext marshals a continuation back onto it (e.g. WPF's
// Dispatcher-backed context). Used to prove a streaming analysis pipeline
// never captures/uses whatever SynchronizationContext is ambient on the
// thread that started enumerating it - the exact mechanism behind a real,
// shipped UI-freeze bug (see docs/UI_RESPONSIVENESS_AUDIT.md): a pipeline
// missing ConfigureAwait(false) on an internal `await foreach`, invoked
// directly from a UI thread, has every per-frame continuation (the actual
// CPU-bound OpenCvSharp work) posted back onto that UI thread's dispatcher
// queue instead of running on a thread-pool thread, freezing the UI -
// Cancel button included - for the run's whole duration. Base
// SynchronizationContext.Post/Send still runs the delegate (via
// ThreadPool.QueueUserWorkItem / direct invocation respectively), so a test
// using this never hangs even if a regression makes it fire.
internal sealed class SynchronizationContextSpy : SynchronizationContext
{
    private int _postCount;
    private int _sendCount;

    public int PostCount => _postCount;

    public int SendCount => _sendCount;

    public override void Post(SendOrPostCallback d, object? state)
    {
        Interlocked.Increment(ref _postCount);
        base.Post(d, state);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        Interlocked.Increment(ref _sendCount);
        base.Send(d, state);
    }
}
