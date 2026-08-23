using SceneForge.Media.Detection;

namespace SceneForge.Media.Tests.Detection;

public class TransitionDetectionTests
{
    [Fact]
    public void Duration_IsEndMinusStart()
    {
        var detection = new TransitionDetection
        {
            Type = TransitionType.Dissolve,
            Start = TimeSpan.FromSeconds(1),
            Peak = TimeSpan.FromSeconds(1.5),
            End = TimeSpan.FromSeconds(2),
            BoundaryTimestamp = TimeSpan.FromSeconds(1.5),
            Confidence = 0.9,
            ContributingSignals = new Dictionary<string, double> { ["StructuralDifference"] = 0.4 },
            DiagnosticReason = "test",
        };

        Assert.Equal(TimeSpan.FromSeconds(1), detection.Duration);
    }
}
