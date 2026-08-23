using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;

namespace SceneForge.Media.Tests.TestSupport;

// Hand-builds minimal, always-"accepted" CleanClips for Planning tests -
// TimelinePlanner only ever reads Range, SourceSceneIndex, and ClusterId
// (never Score or Descriptor), so this fills every other required field
// with a fixed, valid placeholder rather than reusing CleanClipExtractor's
// real scoring pipeline.
internal static class CleanClipBuilder
{
    private static readonly string[] Factors =
    [
        "Duration", "Sharpness", "Stability", "Exposure",
        "FreezeRisk", "TransitionDistance", "OverlaySuspicion", "Overall",
    ];

    public static CleanClip Create(
        double startSeconds,
        double durationSeconds,
        int sourceSceneIndex = 0,
        int? clusterId = null,
        ulong perceptualHash = 0)
    {
        var range = new TimeRange(TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(startSeconds + durationSeconds));

        return new CleanClip
        {
            Range = range,
            SourceSceneIndex = sourceSceneIndex,
            ClusterId = clusterId,
            Score = new ClipScore
            {
                Duration = 1.0,
                Sharpness = 1.0,
                Stability = 1.0,
                Exposure = 1.0,
                FreezeRisk = 0.0,
                TransitionDistance = 1.0,
                OverlaySuspicion = 0.0,
                Overall = 1.0,
                Accepted = true,
                Reasons = Factors.Select(f => new ScoreReason
                {
                    Factor = f,
                    Passed = true,
                    Code = null,
                    Detail = "placeholder",
                }).ToList(),
            },
            Descriptor = new PerceptualDescriptor
            {
                PerceptualHash = perceptualHash,
                ColorHistogram = [],
                EdgeHistogram = [],
                Motion = MotionClass.Static,
            },
        };
    }
}
