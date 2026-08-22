using SceneForge.Media.Domain;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Sampling;

// Exercises the real FrameSampler orchestration (bounded-channel
// producer/consumer, ArrayPool-backed FrameSample lifetime, cancellation,
// backpressure, progress, error propagation) against a faked
// IFrameSamplingProcessLauncher so none of this depends on a real ffmpeg
// binary being present. FrameSamplerIntegrationTests separately proves the
// real command line against real ffmpeg.
public sealed class FrameSamplerTests : IDisposable
{
    private readonly DirectoryInfo _tempDirectory = Directory.CreateTempSubdirectory("sceneforge-framesampler-");

    public void Dispose() => _tempDirectory.Delete(recursive: true);

    [Fact]
    public async Task SampleAsync_HappyPath_EmitsFramesWithSequentialIndexAndExactTimestamps()
    {
        const int totalFrames = 5;
        const double fps = 2.0;
        var dimensions = FrameDimensions.ForTargetWidth(640, 360, 64, FrameSamplePixelFormat.Gray8);

        var launcher = new FakeFrameSamplingProcessLauncher((_, _) => CreateFakeProcess(dimensions.ByteLength, totalFrames, fps));
        var sampler = CreateSampler(640, 360, launcher);
        var options = new FrameSamplingOptions { AnalysisWidthPixels = 64, SampleFramesPerSecond = fps, PixelFormat = FrameSamplePixelFormat.Gray8 };
        var inputPath = CreateInputFile("clip.mp4");

        var frames = await CollectAsync(sampler.SampleAsync(inputPath, options, progress: null, CancellationToken.None));

        Assert.Equal(totalFrames, frames.Count);
        for (var i = 0; i < totalFrames; i++)
        {
            Assert.Equal(i, frames[i].FrameIndex);
            Assert.Equal(TimeSpan.FromSeconds(i / fps), frames[i].Timestamp);
            Assert.Equal(dimensions.Width, frames[i].Width);
            Assert.Equal(dimensions.Height, frames[i].Height);
            Assert.Equal(dimensions.ByteLength, frames[i].ByteLength);
            frames[i].Dispose();
        }
    }

    [Fact]
    public async Task SampleAsync_MediaInfoHasNoVideoStream_ThrowsWithoutLaunchingFfmpeg()
    {
        var mediaInfo = new MediaInfo { FilePath = "x", FormatName = "wav", Duration = TimeSpan.FromSeconds(1), VideoStreams = [], AudioStreams = [] };
        var launcher = new FakeFrameSamplingProcessLauncher((_, _) => throw new InvalidOperationException("ffmpeg must not be launched when there is no video stream."));
        var sampler = new FrameSampler(new FakeFfmpegToolLocator(), FakeFfprobeService.ReturningMediaInfo(mediaInfo), launcher);
        var inputPath = CreateInputFile("audio.mp4");

        await Assert.ThrowsAsync<FrameSamplingException>(
            () => CollectAsync(sampler.SampleAsync(inputPath, new FrameSamplingOptions(), null, CancellationToken.None)));

        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task SampleAsync_FfmpegExitsNonZero_ThrowsFrameSamplingException()
    {
        var dimensions = FrameDimensions.ForTargetWidth(640, 360, 64, FrameSamplePixelFormat.Gray8);
        var launcher = new FakeFrameSamplingProcessLauncher((_, _) => CreateFakeProcess(dimensions.ByteLength, totalFrames: 2, fps: 2.0, exitCode: 1));
        var sampler = CreateSampler(640, 360, launcher);
        var options = new FrameSamplingOptions { AnalysisWidthPixels = 64, PixelFormat = FrameSamplePixelFormat.Gray8 };
        var inputPath = CreateInputFile("clip.mp4");

        var exception = await Assert.ThrowsAsync<FrameSamplingException>(
            () => CollectAsync(sampler.SampleAsync(inputPath, options, null, CancellationToken.None)));

        Assert.Contains("exited with code 1", exception.Message);
    }

    [Fact]
    public async Task SampleAsync_FrameWithoutMatchingShowinfoTimestamp_ThrowsFrameSamplingException()
    {
        var dimensions = FrameDimensions.ForTargetWidth(640, 360, 64, FrameSamplePixelFormat.Gray8);
        var launcher = new FakeFrameSamplingProcessLauncher((_, _) => new FakeFrameSamplingProcess(
            new SyntheticFrameSourceStream(dimensions.ByteLength, totalFrames: 3),
            new SyntheticShowInfoTextReader(totalFrames: 1, fps: 2.0)));
        var sampler = CreateSampler(640, 360, launcher);
        var options = new FrameSamplingOptions { AnalysisWidthPixels = 64, PixelFormat = FrameSamplePixelFormat.Gray8 };
        var inputPath = CreateInputFile("clip.mp4");

        var exception = await Assert.ThrowsAsync<FrameSamplingException>(
            () => CollectAsync(sampler.SampleAsync(inputPath, options, null, CancellationToken.None)));

        Assert.Contains("no matching", exception.Message);
    }

    [Fact]
    public async Task SampleAsync_BuildsExpectedFfmpegArguments()
    {
        var dimensions = FrameDimensions.ForTargetWidth(640, 360, 64, FrameSamplePixelFormat.Gray8);
        var launcher = new FakeFrameSamplingProcessLauncher((_, _) => CreateFakeProcess(dimensions.ByteLength, totalFrames: 1, fps: 2.0));
        var sampler = CreateSampler(640, 360, launcher);
        var options = new FrameSamplingOptions { AnalysisWidthPixels = 64, SampleFramesPerSecond = 2.0, PixelFormat = FrameSamplePixelFormat.Gray8 };
        var inputPath = CreateInputFile("clip.mp4");

        foreach (var frame in await CollectAsync(sampler.SampleAsync(inputPath, options, null, CancellationToken.None)))
        {
            frame.Dispose();
        }

        var request = Assert.Single(launcher.Requests);
        Assert.Equal("ffmpeg.exe", request.FfmpegPath);

        var arguments = request.Arguments;
        var inputIndex = arguments.ToList().IndexOf("-i");
        Assert.True(inputIndex >= 0);
        Assert.Equal(Path.GetFullPath(inputPath), arguments[inputIndex + 1]);
        Assert.Contains("fps=2,showinfo,scale=64:36", arguments);
        Assert.Contains("gray", arguments);
        Assert.Equal("pipe:1", arguments[^1]);
    }

    [Fact]
    public async Task SampleAsync_ExternalCancellation_ThrowsAndKillsProcess()
    {
        var dimensions = FrameDimensions.ForTargetWidth(640, 360, 64, FrameSamplePixelFormat.Gray8);
        FakeFrameSamplingProcess? process = null;
        var launcher = new FakeFrameSamplingProcessLauncher((_, _) => process = CreateFakeProcess(dimensions.ByteLength, totalFrames: 100_000, fps: 30.0));
        var sampler = CreateSampler(640, 360, launcher);
        var options = new FrameSamplingOptions { AnalysisWidthPixels = 64, PixelFormat = FrameSamplePixelFormat.Gray8, ChannelCapacity = 1 };
        var inputPath = CreateInputFile("clip.mp4");

        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var frame in sampler.SampleAsync(inputPath, options, null, cts.Token))
            {
                frame.Dispose();
                cts.Cancel();
            }
        });

        Assert.NotNull(process);
        Assert.True(process!.KillRequested);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task SampleAsync_ConsumerStopsEnumeratingEarly_StillKillsAndDisposesProcess()
    {
        var dimensions = FrameDimensions.ForTargetWidth(640, 360, 64, FrameSamplePixelFormat.Gray8);
        FakeFrameSamplingProcess? process = null;
        var launcher = new FakeFrameSamplingProcessLauncher((_, _) => process = CreateFakeProcess(dimensions.ByteLength, totalFrames: 100_000, fps: 30.0));
        var sampler = CreateSampler(640, 360, launcher);
        var options = new FrameSamplingOptions { AnalysisWidthPixels = 64, PixelFormat = FrameSamplePixelFormat.Gray8, ChannelCapacity = 2 };
        var inputPath = CreateInputFile("clip.mp4");

        await foreach (var frame in sampler.SampleAsync(inputPath, options, null, CancellationToken.None))
        {
            frame.Dispose();
            break;
        }

        Assert.NotNull(process);
        Assert.True(process!.KillRequested);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task SampleAsync_SlowConsumer_ProducerIsBoundedByChannelCapacity()
    {
        const int totalFrames = 5000;
        var dimensions = FrameDimensions.ForTargetWidth(640, 360, 64, FrameSamplePixelFormat.Gray8);
        SyntheticFrameSourceStream? sourceStream = null;
        var launcher = new FakeFrameSamplingProcessLauncher((_, _) =>
        {
            sourceStream = new SyntheticFrameSourceStream(dimensions.ByteLength, totalFrames);
            return new FakeFrameSamplingProcess(sourceStream, new SyntheticShowInfoTextReader(totalFrames, 30.0));
        });
        var sampler = CreateSampler(640, 360, launcher);
        var options = new FrameSamplingOptions { AnalysisWidthPixels = 64, PixelFormat = FrameSamplePixelFormat.Gray8, ChannelCapacity = 2 };
        var inputPath = CreateInputFile("clip.mp4");

        await using var enumerator = sampler.SampleAsync(inputPath, options, null, CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        enumerator.Current.Dispose();

        // The producer is instant (in-memory, no real I/O) - if the channel
        // weren't bounding it, it would race through all 5000 frames almost
        // immediately. With capacity 2 and a consumer that has stopped
        // pulling, it can only ever be a couple of frames ahead.
        await Task.Delay(200);

        Assert.NotNull(sourceStream);
        Assert.True(
            sourceStream!.FramesProduced < 50,
            $"Expected the producer to be blocked by channel backpressure, but it produced {sourceStream.FramesProduced} of {totalFrames} frames.");
    }

    [Fact]
    public async Task SampleAsync_ReportsProgressPerEmittedFrame()
    {
        const int totalFrames = 4;
        const double fps = 2.0;
        var dimensions = FrameDimensions.ForTargetWidth(640, 360, 64, FrameSamplePixelFormat.Gray8);
        var launcher = new FakeFrameSamplingProcessLauncher((_, _) => CreateFakeProcess(dimensions.ByteLength, totalFrames, fps));
        var sampler = CreateSampler(640, 360, launcher);
        var options = new FrameSamplingOptions { AnalysisWidthPixels = 64, SampleFramesPerSecond = fps, PixelFormat = FrameSamplePixelFormat.Gray8 };
        var inputPath = CreateInputFile("clip.mp4");
        var progress = new RecordingProgress<FrameSamplingProgress>();

        foreach (var frame in await CollectAsync(sampler.SampleAsync(inputPath, options, progress, CancellationToken.None)))
        {
            frame.Dispose();
        }

        Assert.Equal(totalFrames, progress.Reports.Count);
        Assert.Equal(Enumerable.Range(1, totalFrames), progress.Reports.Select(r => r.FramesEmitted));
        Assert.Equal(TimeSpan.FromSeconds((totalFrames - 1) / fps), progress.Reports[^1].LastSourceTimestamp);
    }

    private static FrameSampler CreateSampler(int videoWidth, int videoHeight, FakeFrameSamplingProcessLauncher launcher)
    {
        var mediaInfo = CreateMediaInfo(videoWidth, videoHeight);
        return new FrameSampler(new FakeFfmpegToolLocator(), FakeFfprobeService.ReturningMediaInfo(mediaInfo), launcher);
    }

    private static MediaInfo CreateMediaInfo(int width, int height) => new()
    {
        FilePath = "input.mp4",
        FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
        Duration = TimeSpan.FromSeconds(10),
        VideoStreams = [CreateVideoStream(width, height)],
        AudioStreams = [],
    };

    private static VideoStreamInfo CreateVideoStream(int width, int height) => new()
    {
        Index = 0,
        CodecName = "h264",
        Width = width,
        Height = height,
        AverageFrameRate = new RationalFrameRate(30, 1),
        RealBaseFrameRate = new RationalFrameRate(30, 1),
        IsVariableFrameRate = false,
        RotationDegrees = 0,
    };

    private static FakeFrameSamplingProcess CreateFakeProcess(int frameByteLength, int totalFrames, double fps, int exitCode = 0) => new(
        new SyntheticFrameSourceStream(frameByteLength, totalFrames),
        new SyntheticShowInfoTextReader(totalFrames, fps),
        exitCode);

    private static async Task<List<FrameSample>> CollectAsync(IAsyncEnumerable<FrameSample> frames)
    {
        var result = new List<FrameSample>();
        await foreach (var frame in frames)
        {
            result.Add(frame);
        }

        return result;
    }

    private string CreateInputFile(string fileName)
    {
        var path = Path.Combine(_tempDirectory.FullName, fileName);
        File.WriteAllBytes(path, []);
        return path;
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = [];

        public void Report(T value) => Reports.Add(value);
    }
}
