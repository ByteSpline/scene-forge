using SceneForge.Media.Domain;
using SceneForge.Media.Extraction.Signals;

namespace SceneForge.Media.Extraction.Scoring;

// Pure, deterministic scoring: given one candidate's TimeRange, the
// ClipFrameMetrics of every sampled frame that falls within it, and its
// distance to the nearest excluded interval, computes all seven factors
// plus Overall and Accepted. No I/O, no OpenCvSharp - fully testable
// against hand-built ClipFrameMetrics lists, same "deterministic given the
// same inputs" contract TransitionFuser established for Phase 6.
internal static class ClipScorer
{
    public static ClipScore Score(
        TimeRange candidateRange,
        IReadOnlyList<ClipFrameMetrics> framesInWindow,
        TimeSpan distanceToNearestExclusion,
        CleanClipScoringOptions options)
    {
        ArgumentNullException.ThrowIfNull(framesInWindow);
        ArgumentNullException.ThrowIfNull(options);

        var durationScore = DurationScore(candidateRange.Duration, options);
        var durationPassed = candidateRange.Duration >= options.MinClipDuration && candidateRange.Duration <= options.MaxClipDuration;

        var meanSharpness = Average(framesInWindow, f => f.Sharpness);
        var sharpnessScore = Clamp01(meanSharpness / options.SharpnessReferenceValue);

        var meanStructuralDifference = MotionClassifier.MeanStructuralDifference(framesInWindow);
        var stabilityScore = Clamp01(1.0 - (meanStructuralDifference / options.StabilityReferenceValue));

        var meanLuminance = Average(framesInWindow, f => f.MeanLuminance);
        var meanBlackScore = Average(framesInWindow, f => f.BlackScore);
        var meanWhiteScore = Average(framesInWindow, f => f.WhiteScore);
        var exposureScore = Clamp01((1.0 - (2.0 * Math.Abs(meanLuminance - 0.5))) * (1.0 - meanBlackScore) * (1.0 - meanWhiteScore));

        var freezeRisk = framesInWindow.Count == 0
            ? 1.0
            : framesInWindow.Count(f => f.StructuralDifferenceFromPrevious < options.FreezeNearZeroThreshold) / (double)framesInWindow.Count;

        var transitionDistanceScore = Clamp01(distanceToNearestExclusion / options.TransitionSafeDistance);

        var meanBorderDensity = Average(framesInWindow, f => f.BorderEdgeDensity);
        var meanInteriorDensity = Average(framesInWindow, f => f.InteriorEdgeDensity);
        var overlayRatio = meanBorderDensity / Math.Max(meanInteriorDensity, 1e-6);
        var overlaySuspicion = Clamp01((overlayRatio - 1.0) / (options.OverlayRatioReference - 1.0));

        var reasons = new List<ScoreReason>
        {
            Reason("Duration", durationPassed, RejectionReason.DurationOutOfRange,
                $"duration {candidateRange.Duration.TotalSeconds:F2}s within [{options.MinClipDuration.TotalSeconds:F2}s, {options.MaxClipDuration.TotalSeconds:F2}s]"),
            Reason("Sharpness", sharpnessScore >= options.MinAcceptableFactorScore, RejectionReason.InsufficientSharpness,
                $"mean Laplacian variance {meanSharpness:F2} vs reference {options.SharpnessReferenceValue:F2} (score {sharpnessScore:F2})"),
            Reason("Stability", stabilityScore >= options.MinAcceptableFactorScore, RejectionReason.UnstableMotion,
                $"mean structural difference {meanStructuralDifference:F4} vs reference {options.StabilityReferenceValue:F4} (score {stabilityScore:F2})"),
            Reason("Exposure", exposureScore >= options.MinAcceptableFactorScore, RejectionReason.PoorExposure,
                $"mean luminance {meanLuminance:F2}, black {meanBlackScore:F2}, white {meanWhiteScore:F2} (score {exposureScore:F2})"),
            Reason("FreezeRisk", freezeRisk <= options.FreezeRiskRejectionThreshold, RejectionReason.HighFreezeRisk,
                $"near-identical frame fraction {freezeRisk:F2} vs threshold {options.FreezeRiskRejectionThreshold:F2}"),
            Reason("TransitionDistance", transitionDistanceScore >= options.MinAcceptableFactorScore, RejectionReason.TooCloseToExclusion,
                $"nearest exclusion {distanceToNearestExclusion.TotalSeconds:F2}s away vs safe distance {options.TransitionSafeDistance.TotalSeconds:F2}s (score {transitionDistanceScore:F2})"),
            Reason("OverlaySuspicion", overlaySuspicion <= options.OverlaySuspicionRejectionThreshold, RejectionReason.SuspectedOverlay,
                $"border/interior edge ratio {overlayRatio:F2} vs reference {options.OverlayRatioReference:F2} (score {overlaySuspicion:F2})"),
        };

        var overall = WeightedOverall(durationScore, sharpnessScore, stabilityScore, exposureScore, freezeRisk, transitionDistanceScore, overlaySuspicion, options);
        var overallPassed = overall >= options.AcceptanceThreshold;
        reasons.Add(Reason("Overall", overallPassed, RejectionReason.LowOverallScore,
            $"weighted overall {overall:F2} vs acceptance threshold {options.AcceptanceThreshold:F2}"));

        return new ClipScore
        {
            Duration = durationScore,
            Sharpness = sharpnessScore,
            Stability = stabilityScore,
            Exposure = exposureScore,
            FreezeRisk = freezeRisk,
            TransitionDistance = transitionDistanceScore,
            OverlaySuspicion = overlaySuspicion,
            Overall = overall,
            Accepted = reasons.All(r => r.Passed),
            Reasons = reasons,
        };
    }

    private static double DurationScore(TimeSpan duration, CleanClipScoringOptions options)
    {
        var range = options.MaxClipDuration - options.MinClipDuration;
        if (range <= TimeSpan.Zero)
        {
            return 1.0;
        }

        return Clamp01((duration - options.MinClipDuration).Ticks / (double)range.Ticks);
    }

    private static double WeightedOverall(
        double durationScore,
        double sharpnessScore,
        double stabilityScore,
        double exposureScore,
        double freezeRisk,
        double transitionDistanceScore,
        double overlaySuspicion,
        CleanClipScoringOptions options)
    {
        var weights = new[]
        {
            options.DurationWeight,
            options.SharpnessWeight,
            options.StabilityWeight,
            options.ExposureWeight,
            options.FreezeRiskWeight,
            options.TransitionDistanceWeight,
            options.OverlaySuspicionWeight,
        };
        var goodness = new[]
        {
            durationScore,
            sharpnessScore,
            stabilityScore,
            exposureScore,
            1.0 - freezeRisk,
            transitionDistanceScore,
            1.0 - overlaySuspicion,
        };

        var totalWeight = weights.Sum();
        if (totalWeight <= 0)
        {
            return Clamp01(goodness.Average());
        }

        var weightedSum = 0.0;
        for (var i = 0; i < weights.Length; i++)
        {
            weightedSum += weights[i] * goodness[i];
        }

        return Clamp01(weightedSum / totalWeight);
    }

    private static ScoreReason Reason(string factor, bool passed, RejectionReason code, string detail) => new()
    {
        Factor = factor,
        Passed = passed,
        Code = passed ? null : code,
        Detail = detail,
    };

    private static double Average(IReadOnlyList<ClipFrameMetrics> frames, Func<ClipFrameMetrics, double> selector) =>
        frames.Count == 0 ? 0.0 : frames.Average(selector);

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);
}
