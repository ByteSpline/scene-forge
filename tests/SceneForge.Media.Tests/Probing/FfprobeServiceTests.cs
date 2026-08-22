using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Validation;

namespace SceneForge.Media.Tests.Probing;

public sealed class FfprobeServiceTests : IDisposable
{
    private readonly DirectoryInfo _tempDirectory = Directory.CreateTempSubdirectory("sceneforge-ffprobe-");

    public void Dispose() => _tempDirectory.Delete(recursive: true);

    [Fact]
    public async Task ProbeAsync_InputFileDoesNotExist_ThrowsBeforeInvokingProcessRunner()
    {
        var runner = FakeProcessRunner.ReturningResult(JsonResult(ReadFixture("video_audio.json")));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var missingPath = Path.Combine(_tempDirectory.FullName, "missing.mp4");

        var exception = await Assert.ThrowsAsync<MediaValidationException>(() => service.ProbeAsync(missingPath, CancellationToken.None));

        Assert.Equal(MediaValidationFailureReason.FileNotFound, exception.Reason);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task ProbeAsync_PassesExpectedArguments()
    {
        var runner = FakeProcessRunner.ReturningResult(JsonResult(ReadFixture("video_audio.json")));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        await service.ProbeAsync(inputPath, CancellationToken.None);

        var request = Assert.Single(runner.Requests);
        Assert.Equal("ffprobe.exe", request.FileName);
        Assert.Equal(["-v", "error", "-print_format", "json", "-show_format", "-show_streams", inputPath], request.Arguments);
    }

    [Fact]
    public async Task ProbeAsync_NonZeroExitCode_ThrowsFfprobeExecutionExceptionWithStderr()
    {
        var runner = FakeProcessRunner.ReturningResult(new ProcessExecutionResult
        {
            ExitCode = 1,
            StandardOutput = "{}",
            StandardError = "moov atom not found",
            Elapsed = TimeSpan.FromMilliseconds(5),
        });
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("corrupt.mp4");

        var exception = await Assert.ThrowsAsync<FfprobeExecutionException>(() => service.ProbeAsync(inputPath, CancellationToken.None));

        Assert.Equal(1, exception.ExitCode);
        Assert.Contains("moov atom not found", exception.StandardError);
    }

    [Fact]
    public async Task ProbeAsync_MalformedJson_ThrowsFfprobeExecutionException()
    {
        var runner = FakeProcessRunner.ReturningResult(JsonResult("{ this is not valid json"));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        await Assert.ThrowsAsync<FfprobeExecutionException>(() => service.ProbeAsync(inputPath, CancellationToken.None));
    }

    [Fact]
    public async Task ProbeAsync_NoStreams_ThrowsNoMediaStreams()
    {
        var runner = FakeProcessRunner.ReturningResult(JsonResult("""{"streams":[],"format":{"format_name":"mov","duration":"1.0","nb_streams":0}}"""));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        var exception = await Assert.ThrowsAsync<MediaValidationException>(() => service.ProbeAsync(inputPath, CancellationToken.None));

        Assert.Equal(MediaValidationFailureReason.NoMediaStreams, exception.Reason);
    }

    [Fact]
    public async Task ProbeAsync_DurationMissingEverywhere_ThrowsUnknownDuration()
    {
        var json = """
        {
            "streams": [
                { "index": 0, "codec_name": "aac", "codec_type": "audio", "sample_rate": "44100", "channels": 1 }
            ],
            "format": { "format_name": "mov", "nb_streams": 1 }
        }
        """;
        var runner = FakeProcessRunner.ReturningResult(JsonResult(json));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        var exception = await Assert.ThrowsAsync<MediaValidationException>(() => service.ProbeAsync(inputPath, CancellationToken.None));

        Assert.Equal(MediaValidationFailureReason.UnknownDuration, exception.Reason);
    }

    [Fact]
    public async Task ProbeAsync_DurationFallsBackToStreamDurationTsWhenFormatDurationMissing()
    {
        var json = """
        {
            "streams": [
                { "index": 0, "codec_name": "aac", "codec_type": "audio", "sample_rate": "44100", "channels": 1,
                  "time_base": "1/44100", "duration_ts": 88200 }
            ],
            "format": { "format_name": "mov", "nb_streams": 1 }
        }
        """;
        var runner = FakeProcessRunner.ReturningResult(JsonResult(json));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        var mediaInfo = await service.ProbeAsync(inputPath, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(2), mediaInfo.Duration);
    }

    [Fact]
    public async Task ProbeAsync_TypicalVideoAndAudioFile_MapsFieldsCorrectly()
    {
        var runner = FakeProcessRunner.ReturningResult(JsonResult(ReadFixture("video_audio.json")));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        var mediaInfo = await service.ProbeAsync(inputPath, CancellationToken.None);

        Assert.Equal(inputPath, mediaInfo.FilePath);
        Assert.Equal("mov,mp4,m4a,3gp,3g2,mj2", mediaInfo.FormatName);
        Assert.Equal(TimeSpan.FromSeconds(2), mediaInfo.Duration);
        Assert.Equal(127912, mediaInfo.BitRateBitsPerSecond);

        var video = Assert.Single(mediaInfo.VideoStreams);
        Assert.Equal("h264", video.CodecName);
        Assert.Equal(320, video.Width);
        Assert.Equal(240, video.Height);
        Assert.Equal("yuv420p", video.PixelFormat);
        Assert.Equal(25.0, video.AverageFrameRate.ToDouble());
        Assert.Equal(25.0, video.RealBaseFrameRate.ToDouble());
        Assert.False(video.IsVariableFrameRate);
        Assert.Equal(0, video.RotationDegrees);
        Assert.Equal(TimeSpan.FromSeconds(2), video.Duration);

        var audio = Assert.Single(mediaInfo.AudioStreams);
        Assert.Equal("aac", audio.CodecName);
        Assert.Equal(44100, audio.SampleRateHz);
        Assert.Equal(1, audio.Channels);
        Assert.Equal("mono", audio.ChannelLayout);

        Assert.Same(video, mediaInfo.PrimaryVideoStream);
        Assert.Same(audio, mediaInfo.PrimaryAudioStream);
    }

    [Fact]
    public async Task ProbeAsync_AudioOnlyFile_HasNoVideoStreams()
    {
        var runner = FakeProcessRunner.ReturningResult(JsonResult(ReadFixture("audio_only.json")));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.m4a");

        var mediaInfo = await service.ProbeAsync(inputPath, CancellationToken.None);

        Assert.Empty(mediaInfo.VideoStreams);
        Assert.Null(mediaInfo.PrimaryVideoStream);
        Assert.Single(mediaInfo.AudioStreams);
    }

    [Fact]
    public async Task ProbeAsync_RotationViaDisplayMatrix_NormalizesNegativeDegrees()
    {
        var json = """
        {
            "streams": [
                { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 480, "height": 640,
                  "r_frame_rate": "30/1", "avg_frame_rate": "30/1", "duration": "1.0",
                  "side_data_list": [ { "side_data_type": "Display Matrix", "rotation": -90.0 } ] }
            ],
            "format": { "format_name": "mov", "duration": "1.0", "nb_streams": 1 }
        }
        """;
        var runner = FakeProcessRunner.ReturningResult(JsonResult(json));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        var mediaInfo = await service.ProbeAsync(inputPath, CancellationToken.None);

        Assert.Equal(270, mediaInfo.PrimaryVideoStream!.RotationDegrees);
    }

    [Fact]
    public async Task ProbeAsync_RotationViaLegacyTag_IsUsedWhenNoSideData()
    {
        var json = """
        {
            "streams": [
                { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 480, "height": 640,
                  "r_frame_rate": "30/1", "avg_frame_rate": "30/1", "duration": "1.0",
                  "tags": { "rotate": "90" } }
            ],
            "format": { "format_name": "mov", "duration": "1.0", "nb_streams": 1 }
        }
        """;
        var runner = FakeProcessRunner.ReturningResult(JsonResult(json));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        var mediaInfo = await service.ProbeAsync(inputPath, CancellationToken.None);

        Assert.Equal(90, mediaInfo.PrimaryVideoStream!.RotationDegrees);
    }

    [Fact]
    public async Task ProbeAsync_DisplayMatrixRotation_TakesPrecedenceOverLegacyTag()
    {
        var json = """
        {
            "streams": [
                { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 480, "height": 640,
                  "r_frame_rate": "30/1", "avg_frame_rate": "30/1", "duration": "1.0",
                  "tags": { "rotate": "90" },
                  "side_data_list": [ { "side_data_type": "Display Matrix", "rotation": 180.0 } ] }
            ],
            "format": { "format_name": "mov", "duration": "1.0", "nb_streams": 1 }
        }
        """;
        var runner = FakeProcessRunner.ReturningResult(JsonResult(json));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        var mediaInfo = await service.ProbeAsync(inputPath, CancellationToken.None);

        Assert.Equal(180, mediaInfo.PrimaryVideoStream!.RotationDegrees);
    }

    [Fact]
    public async Task ProbeAsync_MismatchedRAndAverageFrameRate_FlagsVariableFrameRate()
    {
        var json = """
        {
            "streams": [
                { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 320, "height": 240,
                  "r_frame_rate": "30/1", "avg_frame_rate": "24/1", "duration": "1.0" }
            ],
            "format": { "format_name": "mov", "duration": "1.0", "nb_streams": 1 }
        }
        """;
        var runner = FakeProcessRunner.ReturningResult(JsonResult(json));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        var mediaInfo = await service.ProbeAsync(inputPath, CancellationToken.None);

        Assert.True(mediaInfo.PrimaryVideoStream!.IsVariableFrameRate);
    }

    [Fact]
    public async Task ProbeAsync_MatchingRAndAverageFrameRate_DoesNotFlagVariableFrameRate()
    {
        var json = """
        {
            "streams": [
                { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 320, "height": 240,
                  "r_frame_rate": "30000/1001", "avg_frame_rate": "30000/1001", "duration": "1.0" }
            ],
            "format": { "format_name": "mov", "duration": "1.0", "nb_streams": 1 }
        }
        """;
        var runner = FakeProcessRunner.ReturningResult(JsonResult(json));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        var mediaInfo = await service.ProbeAsync(inputPath, CancellationToken.None);

        Assert.False(mediaInfo.PrimaryVideoStream!.IsVariableFrameRate);
    }

    [Fact]
    public async Task ProbeAsync_VideoStreamMissingWidth_ThrowsInsteadOfDefaultingToZero()
    {
        var json = """
        {
            "streams": [
                { "index": 0, "codec_name": "h264", "codec_type": "video", "height": 240,
                  "r_frame_rate": "25/1", "avg_frame_rate": "25/1", "duration": "1.0" }
            ],
            "format": { "format_name": "mov", "duration": "1.0", "nb_streams": 1 }
        }
        """;
        var runner = FakeProcessRunner.ReturningResult(JsonResult(json));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        var exception = await Assert.ThrowsAsync<FfprobeExecutionException>(() => service.ProbeAsync(inputPath, CancellationToken.None));

        Assert.Contains("width", exception.Message);
    }

    [Fact]
    public async Task ProbeAsync_AudioStreamMissingSampleRate_ThrowsInsteadOfDefaultingToZero()
    {
        var json = """
        {
            "streams": [
                { "index": 0, "codec_name": "aac", "codec_type": "audio", "channels": 2, "duration": "1.0" }
            ],
            "format": { "format_name": "mov", "duration": "1.0", "nb_streams": 1 }
        }
        """;
        var runner = FakeProcessRunner.ReturningResult(JsonResult(json));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        var exception = await Assert.ThrowsAsync<FfprobeExecutionException>(() => service.ProbeAsync(inputPath, CancellationToken.None));

        Assert.Contains("sample_rate", exception.Message);
    }

    [Fact]
    public async Task ProbeAsync_ProcessRunnerTimesOut_ThrowsFfprobeExecutionException()
    {
        var runner = FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(30), string.Empty, string.Empty));
        var service = new FfprobeService(runner, new FakeFfmpegToolLocator());
        var inputPath = CreateInputFile("clip.mp4");

        await Assert.ThrowsAsync<FfprobeExecutionException>(() => service.ProbeAsync(inputPath, CancellationToken.None));
    }

    private string CreateInputFile(string name)
    {
        var path = Path.Combine(_tempDirectory.FullName, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return Path.GetFullPath(path);
    }

    private static ProcessExecutionResult JsonResult(string json) => new()
    {
        ExitCode = 0,
        StandardOutput = json,
        StandardError = string.Empty,
        Elapsed = TimeSpan.FromMilliseconds(5),
    };

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Json", name));
}
