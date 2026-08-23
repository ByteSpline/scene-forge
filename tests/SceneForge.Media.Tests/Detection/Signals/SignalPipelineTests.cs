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

    private static async IAsyncEnumerable<FrameSample> ToAsyncEnumerable(IEnumerable<FrameSample> frames)
    {
        foreach (var frame in frames)
        {
            yield return frame;
        }

        await Task.CompletedTask;
    }
}
