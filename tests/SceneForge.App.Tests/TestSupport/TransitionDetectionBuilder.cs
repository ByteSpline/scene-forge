using SceneForge.Media.Detection;

namespace SceneForge.App.Tests.TestSupport;

internal static class TransitionDetectionBuilder
{
    public static TransitionDetection Build(TimeSpan start, TimeSpan end, TransitionType type = TransitionType.HardCut, double confidence = 0.9) => new()
    {
        Type = type,
        Start = start,
        Peak = start + TimeSpan.FromTicks((end - start).Ticks / 2),
        End = end,
        BoundaryTimestamp = start,
        Confidence = confidence,
        ContributingSignals = new Dictionary<string, double> { ["Test"] = confidence },
        DiagnosticReason = "test fixture detection",
    };
}
