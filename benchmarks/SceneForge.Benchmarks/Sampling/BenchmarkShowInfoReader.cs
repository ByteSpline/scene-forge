using System.Globalization;

namespace SceneForge.Benchmarks.Sampling;

// Generates one showinfo-shaped log line per frame, matching
// BenchmarkFrameSource frame-for-frame.
internal sealed class BenchmarkShowInfoReader : TextReader
{
    private readonly int _totalFrames;
    private readonly double _fps;
    private int _index;

    public BenchmarkShowInfoReader(int totalFrames, double fps)
    {
        _totalFrames = totalFrames;
        _fps = fps;
    }

    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (_index >= _totalFrames)
        {
            return ValueTask.FromResult<string?>(null);
        }

        var ptsTimeSeconds = _index / _fps;
        var line = FormattableString.Invariant($"[Parsed_showinfo_1] n:{_index,4} pts_time:{ptsTimeSeconds.ToString("0.######", CultureInfo.InvariantCulture)} type:P");
        _index++;
        return ValueTask.FromResult<string?>(line);
    }
}
