using System.Globalization;
using System.Text.RegularExpressions;

namespace SceneForge.Media.Sampling;

// Parses ffmpeg's `showinfo` filter log lines (written to stderr, one per
// frame that passes through the filter graph) to recover the *exact* source
// presentation timestamp of each sampled frame. A representative line looks
// like:
//   [Parsed_showinfo_1 @ 0000020f1a2b3c40] n:   3 pts:    360 pts_time:3.6    pos: 123456 fmt:bgr24 sar:1/1 s:384x216 i:P iskey:0 type:P checksum:... plane_checksum:[...]
// Only the pts_time field is needed here; everything else is ignored.
internal static partial class ShowInfoTimestampParser
{
    [GeneratedRegex(@"\bpts_time:(?<seconds>-?[0-9]+(\.[0-9]+)?)")]
    private static partial Regex PtsTimePattern();

    public static bool TryParsePtsTimeSeconds(string line, out TimeSpan timestamp)
    {
        timestamp = default;

        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var match = PtsTimePattern().Match(line);
        if (!match.Success)
        {
            return false;
        }

        if (!double.TryParse(match.Groups["seconds"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds < 0)
        {
            return false;
        }

        timestamp = TimeSpan.FromSeconds(seconds);
        return true;
    }
}
