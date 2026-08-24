using System.Diagnostics;

namespace SceneForge.Accuracy.Evaluation;

// Tracks peak managed memory (GC.GetTotalMemory) and peak process working
// set (Process.WorkingSet64) over the lifetime of a StartAsync/StopAsync
// span by sampling on a short interval - a point-in-time read before/after
// would miss a transient peak in between (e.g. a large frame buffer that is
// allocated and released entirely within one fixture's analysis). No file
// or process I/O of its own, so this is directly unit-testable by wrapping
// a short async delay.
public sealed class ResourceUsageSampler : IAsyncDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(50);

    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _samplingLoop;

    private long _peakManagedMemoryBytes;
    private long _peakWorkingSetBytes;

    public ResourceUsageSampler()
    {
        Sample();
        _samplingLoop = RunAsync(_cancellation.Token);
    }

    public long PeakManagedMemoryBytes => Interlocked.Read(ref _peakManagedMemoryBytes);

    public long PeakWorkingSetBytes => Interlocked.Read(ref _peakWorkingSetBytes);

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        try
        {
            await _samplingLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cancellation.Dispose();
        _currentProcess.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SampleInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Sample();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Sample()
    {
        var managed = GC.GetTotalMemory(forceFullCollection: false);
        InterlockedMax(ref _peakManagedMemoryBytes, managed);

        _currentProcess.Refresh();
        InterlockedMax(ref _peakWorkingSetBytes, _currentProcess.WorkingSet64);
    }

    private static void InterlockedMax(ref long location, long candidate)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref location);
            if (candidate <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref location, candidate, current) != current);
    }
}
