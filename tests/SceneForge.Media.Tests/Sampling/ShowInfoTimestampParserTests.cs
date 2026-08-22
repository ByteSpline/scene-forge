using SceneForge.Media.Sampling;

namespace SceneForge.Media.Tests.Sampling;

public class ShowInfoTimestampParserTests
{
    [Theory]
    [InlineData(
        "[Parsed_showinfo_1 @ 0000020f1a2b3c40] n:   3 pts:    360 pts_time:3.6    pos: 123456 fmt:bgr24 sar:1/1 s:384x216 i:P iskey:0 type:P checksum:1 plane_checksum:[1]",
        3.6)]
    [InlineData(
        "[Parsed_showinfo_1 @ 0000020f1a2b3c40] n:   0 pts:      0 pts_time:0       pos: 0 fmt:bgr24 sar:1/1 s:384x216 i:I iskey:1 type:I checksum:1 plane_checksum:[1]",
        0.0)]
    [InlineData(
        "[Parsed_showinfo_1 @ 0000020f1a2b3c40] n:  10 pts:   1200 pts_time:12 pos: 0 fmt:gray sar:1/1 s:1x1 i:P iskey:0 type:P checksum:1 plane_checksum:[1]",
        12.0)]
    public void TryParsePtsTimeSeconds_ValidShowinfoLine_ReturnsParsedTimestamp(string line, double expectedSeconds)
    {
        var parsed = ShowInfoTimestampParser.TryParsePtsTimeSeconds(line, out var timestamp);

        Assert.True(parsed);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), timestamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("frame=  120 fps= 45 q=-0.0 size=N/A time=00:00:04.00 bitrate=N/A speed=8.98x")]
    [InlineData("[Parsed_showinfo_1 @ 0000020f1a2b3c40] n:   3 pts:    360 pos: 123456 fmt:bgr24")]
    public void TryParsePtsTimeSeconds_LineWithoutPtsTime_ReturnsFalse(string line)
    {
        var parsed = ShowInfoTimestampParser.TryParsePtsTimeSeconds(line, out var timestamp);

        Assert.False(parsed);
        Assert.Equal(TimeSpan.Zero, timestamp);
    }

    [Fact]
    public void TryParsePtsTimeSeconds_NegativePtsTime_ReturnsFalse()
    {
        var parsed = ShowInfoTimestampParser.TryParsePtsTimeSeconds("... pts_time:-1.5 ...", out _);

        Assert.False(parsed);
    }
}
