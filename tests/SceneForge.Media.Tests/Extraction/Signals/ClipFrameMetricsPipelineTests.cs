using SceneForge.Media.Extraction.Signals;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Extraction.Signals;

public class ClipFrameMetricsPipelineTests
{
    [Fact]
    public async Task ComputeAsync_EmptySequence_YieldsNothing()
    {
        var results = new List<ClipFrameMetrics>();

        await foreach (var metrics in ClipFrameMetricsPipeline.ComputeAsync(ToAsyncEnumerable([])))
        {
            results.Add(metrics);
        }

        Assert.Empty(results);
    }

    [Fact]
    public async Task ComputeAsync_SingleFrame_YieldsOneMetricsWithZeroStructuralDifference()
    {
        var frames = new[] { FrameSampleBuilder.SolidColor(10, 10, 10) };

        var results = new List<ClipFrameMetrics>();
        await foreach (var metrics in ClipFrameMetricsPipeline.ComputeAsync(ToAsyncEnumerable(frames)))
        {
            results.Add(metrics);
        }

        var result = Assert.Single(results);
        Assert.Equal(0.0, result.StructuralDifferenceFromPrevious);
    }

    [Fact]
    public async Task ComputeAsync_TwoFrames_YieldsOneMetricsPerFrame()
    {
        var frameA = FrameSampleBuilder.SolidColor(0, 0, 0, frameIndex: 0, timestamp: TimeSpan.FromSeconds(1));
        var frameB = FrameSampleBuilder.SolidColor(255, 255, 255, frameIndex: 1, timestamp: TimeSpan.FromSeconds(1.5));

        var results = new List<ClipFrameMetrics>();
        await foreach (var metrics in ClipFrameMetricsPipeline.ComputeAsync(ToAsyncEnumerable([frameA, frameB])))
        {
            results.Add(metrics);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), results[0].Timestamp);
        Assert.Equal(0.0, results[0].StructuralDifferenceFromPrevious);
        Assert.Equal(TimeSpan.FromSeconds(1.5), results[1].Timestamp);
        Assert.True(results[1].StructuralDifferenceFromPrevious > 0.9);
    }

    [Fact]
    public async Task ComputeAsync_Cancelled_Throws()
    {
        var frames = new[]
        {
            FrameSampleBuilder.SolidColor(1, 1, 1),
            FrameSampleBuilder.SolidColor(2, 2, 2),
        };

        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in ClipFrameMetricsPipeline.ComputeAsync(ToAsyncEnumerable(frames), cts.Token))
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
    // docs/UI_RESPONSIVENESS_AUDIT.md) - the Extraction analogue of
    // SignalPipelineTests' own equivalent test (see its remarks for the
    // full mechanism): ComputeAsync's internal `await foreach` used to omit
    // ConfigureAwait(false), so a caller invoking it directly from a
    // context-capturing thread (a WPF UI thread in production, via
    // CleanClipExtractor) had every per-frame continuation marshaled back
    // onto that thread instead of running on a thread-pool thread.
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
            };

            var results = new List<ClipFrameMetrics>();
            await foreach (var metrics in ClipFrameMetricsPipeline.ComputeAsync(GenuinelyYieldingAsyncEnumerable(frames)).ConfigureAwait(false))
            {
                results.Add(metrics);
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

    // Unlike ToAsyncEnumerable above (synchronous yields, never exercises
    // real context-capture behavior), forces a genuine asynchronous
    // suspension before every frame via its own ConfigureAwait(false), so
    // the suspension mechanism itself never touches whatever
    // SynchronizationContext the test installed.
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
