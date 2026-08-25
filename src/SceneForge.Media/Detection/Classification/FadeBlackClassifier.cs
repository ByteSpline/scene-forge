using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Detection.Classification;

// Finds the window's darkest point (highest BlackScore); if it reaches
// MinPeakBlackScore, looks for a sustained darkening ramp before it
// (FadeToBlack) and/or a sustained lightening ramp after it (FadeFromBlack).
// Both can fire from the same window (a fade-out-to-black-then-fade-in
// sequence produces two separate detections sharing the black frame as
// their shared boundary), matching how the fixture builder constructs that
// case as two independently-timed ffmpeg `fade` filters.
internal sealed class FadeBlackClassifier : ITransitionClassifier
{
    public TransitionType Type => TransitionType.FadeToBlack;

    public IReadOnlyList<TransitionCandidate> Classify(IReadOnlyList<FrameSignalSample> window, TransitionDetectionProfile profile)
    {
        var thresholds = profile.FadeBlack;

        if (window.Count == 0)
        {
            return [];
        }

        List<TransitionCandidate>? results = null;

        var peakIndex = 0;
        for (var i = 1; i < window.Count; i++)
        {
            if (window[i].BlackScore > window[peakIndex].BlackScore)
            {
                peakIndex = i;
            }
        }

        var peak = window[peakIndex];
        if (peak.BlackScore < thresholds.MinPeakBlackScore)
        {
            return [];
        }

        // Walk outward from the peak while BlackScore keeps moving toward
        // it (monotonic, allowing equal steps) - deliberately not bounded
        // by a fixed fraction-of-peak cutoff. BlackScore is highly
        // nonlinear near its extremes (measured against real fade-filter
        // output: a fade can cross from near-baseline to near-full-black,
        // or back, in a single sample interval once close to the black
        // point), so a fixed "must still be above N% of peak" cutoff can
        // terminate the walk after just one step even though the ramp
        // genuinely continues - which is exactly what happened here before
        // this was measured against real content (docs/PHASE_06_REPORT.md).
        var rampInStart = peakIndex;
        while (rampInStart > 0 && window[rampInStart - 1].BlackScore <= window[rampInStart].BlackScore)
        {
            rampInStart--;
        }

        if (peakIndex - rampInStart >= thresholds.MinRampSamples)
        {
            var consistency = TrendConsistency(window, rampInStart, peakIndex, expectDarkening: true);
            if (consistency >= thresholds.MinTrendConsistency)
            {
                (results ??= []).Add(BuildCandidate(
                    TransitionType.FadeToBlack,
                    window[rampInStart].PreviousTimestamp,
                    peak.Timestamp,
                    peak.Timestamp,
                    peak.BlackScore,
                    consistency,
                    "darkening"));
            }
        }

        var rampOutEnd = peakIndex;
        while (rampOutEnd < window.Count - 1 && window[rampOutEnd + 1].BlackScore <= window[rampOutEnd].BlackScore)
        {
            rampOutEnd++;
        }

        if (rampOutEnd - peakIndex >= thresholds.MinRampSamples)
        {
            var consistency = TrendConsistency(window, peakIndex, rampOutEnd, expectDarkening: false);
            if (consistency >= thresholds.MinTrendConsistency)
            {
                (results ??= []).Add(BuildCandidate(
                    TransitionType.FadeFromBlack,
                    peak.Timestamp,
                    peak.Timestamp,
                    window[rampOutEnd].Timestamp,
                    peak.BlackScore,
                    consistency,
                    "lightening"));
            }
        }

        return results ?? [];
    }

    // Fraction of steps in [start, end] whose LuminanceDelta sign agrees
    // with the expected trend direction.
    private static double TrendConsistency(IReadOnlyList<FrameSignalSample> window, int start, int end, bool expectDarkening)
    {
        var total = end - start;
        if (total <= 0)
        {
            return 1.0;
        }

        var agree = 0;
        for (var i = start + 1; i <= end; i++)
        {
            var darkening = window[i].LuminanceDelta <= 0;
            if (darkening == expectDarkening)
            {
                agree++;
            }
        }

        return (double)agree / total;
    }

    private static TransitionCandidate BuildCandidate(
        TransitionType type,
        TimeSpan start,
        TimeSpan peakTimestamp,
        TimeSpan end,
        double peakBlackScore,
        double trendConsistency,
        string direction) => new()
        {
            Type = type,
            Start = start,
            Peak = peakTimestamp,
            End = end,
            Confidence = Math.Clamp((peakBlackScore + trendConsistency) / 2.0, 0.0, 1.0),
            ContributingSignals = new Dictionary<string, double>
            {
                [nameof(FrameSignalSample.BlackScore)] = peakBlackScore,
                [nameof(FrameSignalSample.LuminanceDelta) + "TrendConsistency"] = trendConsistency,
            },
            DiagnosticReason =
            $"Sustained {direction} ramp reaching BlackScore={peakBlackScore:F2} at {peakTimestamp}, " +
            $"{trendConsistency:P0} of frames agreeing with the {direction} trend.",
        };
}
