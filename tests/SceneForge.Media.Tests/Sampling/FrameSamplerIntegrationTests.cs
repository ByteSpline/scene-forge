using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Tooling;
using Xunit.Abstractions;

namespace SceneForge.Media.Tests.Sampling;

// Exercises the real command line - ProcessRunner/FfmpegToolLocator for
// probing, a real spawned ffmpeg process for decoding - end to end against
// the small synthetic fixture generated for Phase 4
// (Fixtures/Media/sample_video_audio.mp4: h264, 320x240, 25 fps, ~2s).
// Skipped whenever tools/ffmpeg is absent, same as every other real-binary
// test in this project.
public class FrameSamplerIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public FrameSamplerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public async Task SampleAsync_RealFfmpegAgainstRealVideoFile_EmitsDownscaledTimestampedFrames()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);

        var sampler = CreateSampler();
        var mediaPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_video_audio.mp4");
        var options = new FrameSamplingOptions
        {
            AnalysisWidthPixels = 64,
            SampleFramesPerSecond = 5.0,
            PixelFormat = FrameSamplePixelFormat.Bgr24,
        };

        var frames = new List<FrameSample>();
        await foreach (var frame in sampler.SampleAsync(mediaPath, options, progress: null, CancellationToken.None))
        {
            frames.Add(frame);
        }

        try
        {
            _output.WriteLine($"Emitted {frames.Count} frames; timestamps: {string.Join(", ", frames.Select(f => f.Timestamp.TotalSeconds.ToString("0.###")))}");

            // ~2s at 5 fps: allow slack for the last partial tick, same
            // tolerance style as the duration checks in
            // FfprobeServiceIntegrationTests.
            Assert.InRange(frames.Count, 8, 12);

            foreach (var frame in frames)
            {
                Assert.Equal(64, frame.Width);
                Assert.Equal(0, frame.Height % 2);
                Assert.Equal(frame.Width * frame.Height * 3, frame.ByteLength);
            }

            for (var i = 1; i < frames.Count; i++)
            {
                Assert.True(frames[i].Timestamp > frames[i - 1].Timestamp, "Timestamps must be strictly increasing.");
            }

            Assert.True(frames[0].Timestamp < TimeSpan.FromSeconds(1));
            Assert.True(frames[^1].Timestamp < TimeSpan.FromSeconds(3));
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    [SkippableFact]
    public async Task SampleAsync_RealFfmpegAgainstInvalidFile_ThrowsFfprobeExecutionException()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);

        var sampler = CreateSampler();
        var invalidPath = Path.Combine(Path.GetTempPath(), $"sceneforge-invalid-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(invalidPath, "not a real media file");

        try
        {
            await Assert.ThrowsAsync<FfprobeExecutionException>(async () =>
            {
                await foreach (var frame in sampler.SampleAsync(invalidPath, new FrameSamplingOptions(), null, CancellationToken.None))
                {
                    frame.Dispose();
                }
            });
        }
        finally
        {
            File.Delete(invalidPath);
        }
    }

    private static FrameSampler CreateSampler()
    {
        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);
        return new FrameSampler(toolLocator, ffprobeService);
    }
}
