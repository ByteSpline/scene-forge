using System.Globalization;

namespace SceneForge.Media.Tests.TestSupport;

// Generates one ffmpeg-showinfo-shaped log line per frame, lazily, matching
// SyntheticFrameSourceStream frame-for-frame - never materializes all lines
// up front.
internal sealed class SyntheticShowInfoTextReader : TextReader
{
    private readonly int _totalFrames;
    private readonly double _fps;
    private int _index;

    public SyntheticShowInfoTextReader(int totalFrames, double fps)
    {
        _totalFrames = totalFrames;
        _fps = fps;
    }

    public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();

        if (_index >= _totalFrames)
        {
            return null;
        }

        var ptsTimeSeconds = _index / _fps;
        var line = FormattableString.Invariant(
            $"[Parsed_showinfo_1 @ 0000000000000000] n:{_index,4} pts:{(long)(ptsTimeSeconds * 1000),6} pts_time:{ptsTimeSeconds.ToString("0.######", CultureInfo.InvariantCulture)} pos: 0 fmt:bgr24 sar:1/1 s:1x1 i:P iskey:0 type:P checksum:0 plane_checksum:[0]");

        _index++;
        return line;
    }
}
