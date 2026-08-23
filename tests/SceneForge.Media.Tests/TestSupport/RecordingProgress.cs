namespace SceneForge.Media.Tests.TestSupport;

// A synchronous IProgress<T> test double - unlike System.Progress<T>, which
// posts each Report through the ThreadPool (or a captured
// SynchronizationContext) and offers no guarantee a queued callback has run
// by the time an awaited caller returns, this records every report
// immediately and in order. Needed by any test driving a synchronous,
// already-completed fake process runner, where System.Progress<T>'s
// asynchronous dispatch would race the test's own assertions.
internal sealed class RecordingProgress<T> : IProgress<T>
{
    public List<T> Reports { get; } = [];

    public void Report(T value) => Reports.Add(value);
}
