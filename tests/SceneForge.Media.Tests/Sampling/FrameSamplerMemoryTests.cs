using SceneForge.Core.Resources;
using SceneForge.Media.Domain;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tests.TestSupport;
using Xunit.Abstractions;

namespace SceneForge.Media.Tests.Sampling;

// Proves CLAUDE.md rule 7 (no full-video buffering; process/discard within
// bounded windows) for the sampling pipeline specifically: managed memory
// retained after a run should not scale with how many frames were sampled,
// because every FrameSample's buffer is pooled and returned as soon as the
// consumer disposes it, and the producer/consumer channel is bounded. Uses
// a synthetic in-process frame source (no real ffmpeg, no real video file)
// so an arbitrarily long "video" can be simulated without the test itself
// needing proportional memory or wall-clock time.
public sealed class FrameSamplerMemoryTests : IDisposable
{
    private const int Width = 384;
    private const int Height = 216;
    private const double Fps = 4.0;

    // A 20,000-frame run at 83 KB/frame represents ~1.66 GB of raw frame
    // data flowing through the pipeline if none of it were ever released.
    private const int ShortRunFrameCount = 2_000;
    private const int LongRunFrameCount = 20_000;

    private readonly DirectoryInfo _tempDirectory = Directory.CreateTempSubdirectory("sceneforge-framesampler-memory-");
    private readonly ITestOutputHelper _output;

    public FrameSamplerMemoryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose() => _tempDirectory.Delete(recursive: true);

    [Fact]
    public async Task SampleAsync_RetainedMemoryStaysApproximatelyFlatAsSimulatedDurationIncreases()
    {
        var shortRunDeltaBytes = await MeasureRetainedMemoryDeltaAsync(ShortRunFrameCount);
        var longRunDeltaBytes = await MeasureRetainedMemoryDeltaAsync(LongRunFrameCount);

        _output.WriteLine($"{ShortRunFrameCount:N0} frames -> retained delta {shortRunDeltaBytes:N0} bytes");
        _output.WriteLine($"{LongRunFrameCount:N0} frames -> retained delta {longRunDeltaBytes:N0} bytes");
        _output.WriteLine($"Raw frame data that passed through the pipeline for the long run: {(long)LongRunFrameCount * Width * Height:N0} bytes");

        const long absoluteCeilingBytes = 10 * 1024 * 1024;
        const long flatnessToleranceBytes = 5 * 1024 * 1024;

        Assert.True(
            longRunDeltaBytes < absoluteCeilingBytes,
            $"A {LongRunFrameCount}-frame run retained {longRunDeltaBytes:N0} bytes after GC, " +
            $"which is not bounded/flat relative to the ~{(long)LongRunFrameCount * Width * Height:N0} bytes " +
            "of raw frame data that passed through the pipeline.");

        Assert.True(
            Math.Abs(longRunDeltaBytes - shortRunDeltaBytes) < flatnessToleranceBytes,
            $"Expected retained memory to stay approximately flat between a {ShortRunFrameCount}-frame run " +
            $"({shortRunDeltaBytes:N0} bytes) and a {LongRunFrameCount}-frame run ({longRunDeltaBytes:N0} bytes) - " +
            "a 10x increase in simulated duration should not proportionally grow retained memory.");
    }

    private async Task<long> MeasureRetainedMemoryDeltaAsync(int totalFrames)
    {
        var mediaInfo = new MediaInfo
        {
            FilePath = "input.mp4",
            FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
            Duration = TimeSpan.FromSeconds(totalFrames / Fps),
            VideoStreams =
            [
                new VideoStreamInfo
                {
                    Index = 0,
                    CodecName = "h264",
                    Width = Width,
                    Height = Height,
                    AverageFrameRate = new RationalFrameRate(30, 1),
                    RealBaseFrameRate = new RationalFrameRate(30, 1),
                    IsVariableFrameRate = false,
                    RotationDegrees = 0,
                },
            ],
            AudioStreams = [],
        };

        var launcher = new FakeFrameSamplingProcessLauncher((_, _) => new FakeFrameSamplingProcess(
            new SyntheticFrameSourceStream(Width * Height, totalFrames),
            new SyntheticShowInfoTextReader(totalFrames, Fps)));
        var sampler = new FrameSampler(new FakeFfmpegToolLocator(), FakeFfprobeService.ReturningMediaInfo(mediaInfo), new AdaptiveResourceGovernor(), launcher);
        var options = new FrameSamplingOptions
        {
            AnalysisWidthPixels = Width,
            SampleFramesPerSecond = Fps,
            PixelFormat = FrameSamplePixelFormat.Gray8,
            ChannelCapacity = 4,
        };

        var inputPath = Path.Combine(_tempDirectory.FullName, $"clip-{totalFrames}.mp4");
        await File.WriteAllBytesAsync(inputPath, []);

        ForceFullCollection();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var framesSeen = 0;
        await foreach (var frame in sampler.SampleAsync(inputPath, options, null, CancellationToken.None))
        {
            // Stand in for a consumer doing real work with the frame before releasing it.
            framesSeen += frame.Span.Length > 0 ? 1 : 0;
            frame.Dispose();
        }

        Assert.Equal(totalFrames, framesSeen);

        ForceFullCollection();
        var after = GC.GetTotalMemory(forceFullCollection: true);

        return Math.Max(0, after - before);
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
