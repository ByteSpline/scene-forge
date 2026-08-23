using System.Globalization;

namespace SceneForge.Media.Rendering.Internal;

// Parses ffmpeg's own '-progress pipe:1' machine-readable key=value stream
// (one key=value pair per line, each block terminated by a 'progress=continue'
// or 'progress=end' line) into RenderProgress updates. Never scrapes
// ffmpeg's human-oriented -stats banner text, which is not a stable,
// parseable format across builds - '-progress' is ffmpeg's own documented
// machine-readable contract for exactly this purpose.
internal sealed class RenderProgressParser
{
    private readonly Dictionary<string, string> _current = [];

    // Feeds one line of ffmpeg's -progress output. Returns a RenderProgress
    // (with EstimatedTimeRemaining left null - the caller fills that in,
    // since it needs the planned total duration this parser does not know
    // about) only on the line that completes a block ('progress=...');
    // every other line just accumulates into the current block and returns
    // null.
    public RenderProgress? Accept(string line, TimeSpan elapsed)
    {
        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return null;
        }

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();
        _current[key] = value;

        if (key != "progress")
        {
            return null;
        }

        var progress = BuildProgress(elapsed, isFinished: value == "end");
        _current.Clear();
        return progress;
    }

    private RenderProgress BuildProgress(TimeSpan elapsed, bool isFinished) => new()
    {
        FrameNumber = ParseLong("frame") ?? 0,
        FramesPerSecond = ParseDouble("fps"),
        OutTime = ParseOutTime(),
        Speed = ParseSpeed(),
        Elapsed = elapsed,
        IsFinished = isFinished,
    };

    private TimeSpan ParseOutTime()
    {
        if (_current.TryGetValue("out_time_us", out var microsecondsText)
            && long.TryParse(microsecondsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
        {
            return TimeSpan.FromTicks(microseconds * (TimeSpan.TicksPerMillisecond / 1000));
        }

        if (_current.TryGetValue("out_time", out var text)
            && TimeSpan.TryParseExact(text, [@"hh\:mm\:ss\.ffffff", @"hh\:mm\:ss\.fff", @"hh\:mm\:ss"], CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return TimeSpan.Zero;
    }

    private double? ParseSpeed()
    {
        if (!_current.TryGetValue("speed", out var text))
        {
            return null;
        }

        var trimmed = text.TrimEnd('x', ' ');
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ? speed : null;
    }

    private long? ParseLong(string key) =>
        _current.TryGetValue(key, out var text) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private double? ParseDouble(string key) =>
        _current.TryGetValue(key, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
