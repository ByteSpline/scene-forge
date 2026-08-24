using SceneForge.Media.Domain;

namespace SceneForge.App.Tests.TestSupport;

// Minimal, valid MediaInfo fixtures - mirrors
// SceneForge.Media.Tests.TestSupport.MediaInfoBuilder's shape (kept local to
// this project rather than shared, since App.Tests has no reference to
// Media.Tests).
internal static class MediaInfoBuilder
{
    public static MediaInfo Video(string filePath, TimeSpan duration) => new()
    {
        FilePath = filePath,
        FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
        Duration = duration,
        VideoStreams =
        [
            new VideoStreamInfo
            {
                Index = 0,
                CodecName = "h264",
                Width = 640,
                Height = 360,
                AverageFrameRate = new RationalFrameRate(30, 1),
                RealBaseFrameRate = new RationalFrameRate(30, 1),
                IsVariableFrameRate = false,
                RotationDegrees = 0,
            },
        ],
        AudioStreams = [],
    };

    public static MediaInfo Audio(string filePath, TimeSpan duration) => new()
    {
        FilePath = filePath,
        FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
        Duration = duration,
        VideoStreams = [],
        AudioStreams =
        [
            new AudioStreamInfo
            {
                Index = 0,
                CodecName = "aac",
                SampleRateHz = 48_000,
                Channels = 2,
            },
        ],
    };
}
