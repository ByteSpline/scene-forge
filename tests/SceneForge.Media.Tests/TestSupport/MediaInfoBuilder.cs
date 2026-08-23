using SceneForge.Media.Domain;

namespace SceneForge.Media.Tests.TestSupport;

// Hand-builds minimal, valid MediaInfo fixtures for tests that need a
// caller-supplied probe result without spawning ffprobe - the same
// "fill every other required field with a fixed placeholder" shape
// CleanClipBuilder already established for CleanClip.
internal static class MediaInfoBuilder
{
    public static MediaInfo CreateVideoWithAudio(
        string filePath = "source.mp4",
        int width = 1280,
        int height = 720,
        double frameRate = 25.0,
        int rotationDegrees = 0,
        double durationSeconds = 60.0,
        string? pixelFormat = "yuv420p") => new()
        {
            FilePath = filePath,
            FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
            Duration = TimeSpan.FromSeconds(durationSeconds),
            VideoStreams =
            [
                new VideoStreamInfo
                {
                    Index = 0,
                    CodecName = "h264",
                    Width = width,
                    Height = height,
                    AverageFrameRate = new RationalFrameRate((long)(frameRate * 1000), 1000),
                    RealBaseFrameRate = new RationalFrameRate((long)(frameRate * 1000), 1000),
                    IsVariableFrameRate = false,
                    RotationDegrees = rotationDegrees,
                    PixelFormat = pixelFormat,
                    Duration = TimeSpan.FromSeconds(durationSeconds),
                },
            ],
            AudioStreams =
            [
                new AudioStreamInfo
                {
                    Index = 1,
                    CodecName = "aac",
                    SampleRateHz = 48_000,
                    Channels = 2,
                    ChannelLayout = "stereo",
                    Duration = TimeSpan.FromSeconds(durationSeconds),
                },
            ],
        };

    public static MediaInfo CreateVideoOnly(
        string filePath = "source.mp4",
        int width = 1280,
        int height = 720,
        double frameRate = 25.0,
        int rotationDegrees = 0,
        double durationSeconds = 60.0) => CreateVideoWithAudio(filePath, width, height, frameRate, rotationDegrees, durationSeconds) with
        {
            AudioStreams = [],
        };
}
