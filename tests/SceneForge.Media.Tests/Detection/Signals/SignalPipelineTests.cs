using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Signals;

public class SignalPipelineTests
{
    private readonly SignalPipeline _pipeline = new();

    [Fact]
    public async Task ComputeAsync_EmptySequence_YieldsNothing()
    {
        var results = new List<FrameSignalSample>();

        await foreach (var sample in _pipeline.ComputeAsync(ToAsyncEnumerable([])))
        {
            results.Add(sample);
        }

        Assert.Empty(results);
    }

    [Fact]
    public async Task ComputeAsync_SingleFrame_YieldsNothing()
    {
        var frames = new[] { FrameSampleBuilder.SolidColor(10, 10, 10) };
        var results = new List<FrameSignalSample>();

        await foreach (var sample in _pipeline.ComputeAsync(ToAsyncEnumerable(frames)))
        {
            results.Add(sample);
        }

        Assert.Empty(results);
    }

    [Fact]
    public async Task ComputeAsync_TwoFrames_YieldsOneSampleWithMatchingTimestamps()
    {
        var frameA = FrameSampleBuilder.SolidColor(0, 0, 0, frameIndex: 0, timestamp: TimeSpan.FromSeconds(1));
        var frameB = FrameSampleBuilder.SolidColor(255, 255, 255, frameIndex: 1, timestamp: TimeSpan.FromSeconds(1.5));

        var results = new List<FrameSignalSample>();
        await foreach (var sample in _pipeline.ComputeAsync(ToAsyncEnumerable([frameA, frameB])))
        {
            results.Add(sample);
        }

        var result = Assert.Single(results);
        Assert.Equal(TimeSpan.FromSeconds(1), result.PreviousTimestamp);
        Assert.Equal(TimeSpan.FromSeconds(1.5), result.Timestamp);
        Assert.True(result.LuminanceDelta > 0.9);
    }

    [Fact]
    public async Task ComputeAsync_ThreeIdenticalFrames_YieldsTwoZeroDeltaSamples()
    {
        var frames = new[]
        {
            FrameSampleBuilder.SolidColor(70, 70, 70, frameIndex: 0, timestamp: TimeSpan.Zero),
            FrameSampleBuilder.SolidColor(70, 70, 70, frameIndex: 1, timestamp: TimeSpan.FromSeconds(0.25)),
            FrameSampleBuilder.SolidColor(70, 70, 70, frameIndex: 2, timestamp: TimeSpan.FromSeconds(0.5)),
        };

        var results = new List<FrameSignalSample>();
        await foreach (var sample in _pipeline.ComputeAsync(ToAsyncEnumerable(frames)))
        {
            results.Add(sample);
        }

        Assert.Equal(2, results.Count);
        Assert.All(results, sample =>
        {
            Assert.Equal(0.0, sample.LuminanceDelta);
            Assert.Equal(0.0, sample.StructuralDifference);
            Assert.Equal(0.0, sample.HsvHistogramDistance, precision: 6);
        });
    }

    [Fact]
    public async Task ComputeAsync_Cancelled_Throws()
    {
        var frames = new[]
        {
            FrameSampleBuilder.SolidColor(1, 1, 1),
            FrameSampleBuilder.SolidColor(2, 2, 2),
            FrameSampleBuilder.SolidColor(3, 3, 3),
        };

        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in _pipeline.ComputeAsync(ToAsyncEnumerable(frames), cts.Token))
                {
                }
            });
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    // Regression coverage for a real, shipped UI-freeze bug (see
    // docs/UI_RESPONSIVENESS_AUDIT.md): ComputeAsync's internal
    // `await foreach` used to omit ConfigureAwait(false), so a caller that
    // invokes it directly from a context-capturing thread (a WPF UI thread
    // in production) had every per-frame continuation - the actual
    // OpenCvSharp signal-extraction work - marshaled back onto that
    // thread's SynchronizationContext instead of running on a thread-pool
    // thread, serializing the whole analysis onto the UI message loop.
    // Installs a spy SynchronizationContext (standing in for WPF's
    // Dispatcher) on this test thread, feeds frames through an upstream
    // enumerable that genuinely suspends (via a ConfigureAwait(false)
    // Task.Delay, so the suspension itself never touches the spy) between
    // each one - forcing ComputeAsync's own internal await to actually
    // capture/restore context if it does not correctly opt out - and
    // asserts the spy never sees a single Post or Send.
    [Fact]
    public async Task ComputeAsync_ConsumedFromAContextCapturingThread_NeverPostsPerFrameWorkBackToThatContext()
    {
        var spy = new SynchronizationContextSpy();
        var original = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(spy);
        try
        {
            var frames = new[]
            {
                FrameSampleBuilder.SolidColor(10, 10, 10, frameIndex: 0, timestamp: TimeSpan.Zero),
                FrameSampleBuilder.SolidColor(20, 20, 20, frameIndex: 1, timestamp: TimeSpan.FromMilliseconds(100)),
                FrameSampleBuilder.SolidColor(30, 30, 30, frameIndex: 2, timestamp: TimeSpan.FromMilliseconds(200)),
            };

            var results = new List<FrameSignalSample>();
            await foreach (var sample in _pipeline.ComputeAsync(GenuinelyYieldingAsyncEnumerable(frames)).ConfigureAwait(false))
            {
                results.Add(sample);
            }

            Assert.Equal(2, results.Count);
            Assert.Equal(0, spy.PostCount);
            Assert.Equal(0, spy.SendCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }
    }

    private static async IAsyncEnumerable<FrameSample> ToAsyncEnumerable(IEnumerable<FrameSample> frames)
    {
        foreach (var frame in frames)
        {
            yield return frame;
        }

        await Task.CompletedTask;
    }

    // Unlike ToAsyncEnumerable above (which yields synchronously and so
    // never exercises real context-capture behavior at all), this forces a
    // genuine asynchronous suspension before every frame - and does so via
    // its own ConfigureAwait(false), so the suspension mechanism itself
    // never touches whatever SynchronizationContext the test installed,
    // isolating the measurement to ComputeAsync's own internal behavior.
    private static async IAsyncEnumerable<FrameSample> GenuinelyYieldingAsyncEnumerable(
        IEnumerable<FrameSample> frames,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            yield return frame;
        }
    }
}
