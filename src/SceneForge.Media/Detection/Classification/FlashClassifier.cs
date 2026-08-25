using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Detection.Classification;

// Same shape as FadeBlackClassifier's ramp-finding but keyed on WhiteScore,
// and gated on total duration rather than trend consistency: a Flash is
// distinguished from a fade-to/from-white purely by being short
// (profile.Flash.MaxDuration) - a brief overexposed/white spike, not a
// deliberate slow fade.
internal sealed class FlashClassifier : ITransitionClassifier
{
    private const double BaselineFraction = 0.3;

    public TransitionType Type => TransitionType.Flash;

    public IReadOnlyList<TransitionCandidate> Classify(IReadOnlyList<FrameSignalSample> window, TransitionDetectionProfile profile)
    {
        var thresholds = profile.Flash;

        if (window.Count == 0)
        {
            return [];
        }

        var peakIndex = 0;
        for (var i = 1; i < window.Count; i++)
        {
            if (window[i].WhiteScore > window[peakIndex].WhiteScore)
            {
                peakIndex = i;
            }
        }

        var peak = window[peakIndex];
        if (peak.WhiteScore < thresholds.MinPeakWhiteScore)
        {
            return [];
        }

        var baseline = peak.WhiteScore * BaselineFraction;

        var left = peakIndex;
        while (left > 0 && window[left - 1].WhiteScore >= baseline)
        {
            left--;
        }

        var right = peakIndex;
        while (right < window.Count - 1 && window[right + 1].WhiteScore >= baseline)
        {
            right++;
        }

        var start = window[left].PreviousTimestamp;
        var end = window[right].Timestamp;
        var duration = end - start;
        if (duration > thresholds.MaxDuration)
        {
            return [];
        }

        return
        [
            new TransitionCandidate
            {
                Type = TransitionType.Flash,
                Start = start,
                Peak = peak.Timestamp,
                End = end,
                Confidence = Math.Clamp(peak.WhiteScore, 0.0, 1.0),
                ContributingSignals = new Dictionary<string, double>
                {
                    [nameof(FrameSignalSample.WhiteScore)] = peak.WhiteScore,
                    ["DurationSeconds"] = duration.TotalSeconds,
                },
                DiagnosticReason =
                    $"Brief WhiteScore spike reaching {peak.WhiteScore:F2} at {peak.Timestamp}, total span {duration.TotalMilliseconds:F0}ms " +
                    $"(<= {thresholds.MaxDuration.TotalMilliseconds:F0}ms threshold for Flash vs. a slower fade-to-white).",
            },
        ];
    }
}
