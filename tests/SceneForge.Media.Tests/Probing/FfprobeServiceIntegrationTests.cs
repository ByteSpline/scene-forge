using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Tooling;
using Xunit;

namespace SceneForge.Media.Tests.Probing;

// Exercises the real pipeline end-to-end (ProcessRunner -> FfmpegToolLocator
// -> real ffprobe.exe -> JSON parsing) against small synthetic fixtures
// generated locally with `ffmpeg -f lavfi` (no network, no copyrighted
// content). Skipped whenever tools/ffmpeg is absent, which is the normal
// state for a fresh clone or CI, since the binaries are never committed.
public class FfprobeServiceIntegrationTests
{
    [SkippableFact]
    public async Task ProbeAsync_RealFfprobeAgainstRealVideoAudioFile_ReturnsAccurateMetadata()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);

        var service = CreateService();
        var mediaPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_video_audio.mp4");

        var mediaInfo = await service.ProbeAsync(mediaPath, CancellationToken.None);

        Assert.Equal("h264", mediaInfo.PrimaryVideoStream!.CodecName);
        Assert.Equal(320, mediaInfo.PrimaryVideoStream.Width);
        Assert.Equal(240, mediaInfo.PrimaryVideoStream.Height);
        Assert.Equal(25.0, mediaInfo.PrimaryVideoStream.AverageFrameRate.ToDouble());
        Assert.False(mediaInfo.PrimaryVideoStream.IsVariableFrameRate);
        Assert.Equal("aac", mediaInfo.PrimaryAudioStream!.CodecName);
        Assert.Equal(44100, mediaInfo.PrimaryAudioStream.SampleRateHz);
        Assert.InRange(mediaInfo.Duration.TotalSeconds, 1.9, 2.1);
    }

    [SkippableFact]
    public async Task ProbeAsync_RealFfprobeAgainstRealAudioOnlyFile_ReturnsAudioMetadataOnly()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);

        var service = CreateService();
        var mediaPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_audio_only.m4a");

        var mediaInfo = await service.ProbeAsync(mediaPath, CancellationToken.None);

        Assert.Empty(mediaInfo.VideoStreams);
        Assert.Equal("aac", mediaInfo.PrimaryAudioStream!.CodecName);
        Assert.Equal(44100, mediaInfo.PrimaryAudioStream.SampleRateHz);
        Assert.InRange(mediaInfo.Duration.TotalSeconds, 0.9, 1.1);
    }

    [SkippableFact]
    public async Task ProbeAsync_RealFfprobeAgainstInvalidFile_ThrowsFfprobeExecutionException()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);

        var service = CreateService();
        var invalidPath = Path.Combine(Path.GetTempPath(), $"sceneforge-invalid-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(invalidPath, "not a real media file");

        try
        {
            var exception = await Assert.ThrowsAsync<FfprobeExecutionException>(() => service.ProbeAsync(invalidPath, CancellationToken.None));
            Assert.NotEqual(0, exception.ExitCode);
        }
        finally
        {
            File.Delete(invalidPath);
        }
    }

    private static FfprobeService CreateService()
    {
        var processRunner = new ProcessRunner();
        return new FfprobeService(processRunner, new FfmpegToolLocator(processRunner));
    }
}
