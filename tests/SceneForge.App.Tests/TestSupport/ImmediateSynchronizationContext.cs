namespace SceneForge.App.Tests.TestSupport;

// A SynchronizationContext that runs every Post/Send callback immediately,
// inline, on the calling thread - standing in for a real single-threaded
// UI dispatcher (WPF's DispatcherSynchronizationContext) in a plain xUnit
// test, so that IProgress<T>.Report callbacks (which post via whatever
// SynchronizationContext.Current was when the Progress<T> was constructed)
// run synchronously and in order relative to the rest of the test, instead
// of falling back to unordered ThreadPool.QueueUserWorkItem scheduling (the
// default when no context is installed) - the latter makes any test that
// depends on "did the final value win over an earlier progress update"
// genuinely racy/flaky, not just theoretically so.
internal sealed class ImmediateSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => d(state);

    public override void Send(SendOrPostCallback d, object? state) => d(state);
}
