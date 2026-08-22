using SceneForge.Media.Domain;

namespace SceneForge.Media.Tests.Domain;

public class MediaInfoTests
{
    [Fact]
    public void PrimaryVideoStream_NoVideoStreams_ReturnsNull()
    {
        var mediaInfo = new MediaInfo
        {
            FilePath = "clip.mp4",
            FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
            Duration = TimeSpan.FromSeconds(1),
            VideoStreams = [],
            AudioStreams = [],
        };

        Assert.Null(mediaInfo.PrimaryVideoStream);
        Assert.Null(mediaInfo.PrimaryAudioStream);
    }

    [Fact]
    public void PrimaryVideoStream_WithStreams_ReturnsFirstOfEachKind()
    {
        var video = new VideoStreamInfo
        {
            Index = 0,
            CodecName = "h264",
            Width = 320,
            Height = 240,
            AverageFrameRate = new RationalFrameRate(25, 1),
            RealBaseFrameRate = new RationalFrameRate(25, 1),
            IsVariableFrameRate = false,
            RotationDegrees = 0,
        };
        var audio = new AudioStreamInfo
        {
            Index = 1,
            CodecName = "aac",
            SampleRateHz = 44100,
            Channels = 2,
        };

        var mediaInfo = new MediaInfo
        {
            FilePath = "clip.mp4",
            FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
            Duration = TimeSpan.FromSeconds(2),
            VideoStreams = [video],
            AudioStreams = [audio],
        };

        Assert.Same(video, mediaInfo.PrimaryVideoStream);
        Assert.Same(audio, mediaInfo.PrimaryAudioStream);
    }
}
