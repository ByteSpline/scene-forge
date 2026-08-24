using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;

namespace SceneForge.App.Tests.TestSupport;

internal static class CleanClipBuilder
{
    public static CleanClip Build(TimeSpan start, TimeSpan end, bool accepted, int sourceSceneIndex = 0) => new()
    {
        Range = new TimeRange(start, end),
        SourceSceneIndex = sourceSceneIndex,
        Score = Score(accepted),
        Descriptor = new PerceptualDescriptor
        {
            PerceptualHash = 0UL,
            ColorHistogram = new float[8],
            EdgeHistogram = new float[8],
            Motion = MotionClass.Static,
        },
        ClusterId = null,
    };

    private static ClipScore Score(bool accepted) => new()
    {
        Duration = 0.8,
        Sharpness = 0.8,
        Stability = 0.8,
        Exposure = 0.8,
        FreezeRisk = 0.1,
        TransitionDistance = 0.8,
        OverlaySuspicion = 0.1,
        Overall = accepted ? 0.8 : 0.2,
        Accepted = accepted,
        Reasons =
        [
            new ScoreReason { Factor = "Duration", Passed = true, Code = null, Detail = "ok" },
            new ScoreReason { Factor = "Sharpness", Passed = true, Code = null, Detail = "ok" },
            new ScoreReason { Factor = "Stability", Passed = true, Code = null, Detail = "ok" },
            new ScoreReason { Factor = "Exposure", Passed = true, Code = null, Detail = "ok" },
            new ScoreReason { Factor = "FreezeRisk", Passed = true, Code = null, Detail = "ok" },
            new ScoreReason { Factor = "TransitionDistance", Passed = true, Code = null, Detail = "ok" },
            new ScoreReason { Factor = "OverlaySuspicion", Passed = true, Code = null, Detail = "ok" },
            new ScoreReason
            {
                Factor = "Overall",
                Passed = accepted,
                Code = accepted ? null : RejectionReason.LowOverallScore,
                Detail = accepted ? "ok" : "below threshold",
            },
        ],
    };
}
