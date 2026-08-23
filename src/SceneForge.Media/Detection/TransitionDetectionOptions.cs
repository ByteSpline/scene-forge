using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Detection;

// Every knob a caller can configure for a transition-detection run: how
// frames are sampled (reuses Sampling.FrameSamplingOptions verbatim - this
// phase adds no new sampling behavior) and which versioned fusion/
// classifier profile to apply. Both are already independently bounded
// (FrameSamplingOptions clamps its own fields; TransitionDetectionProfile
// clamps its own), so this record needs no additional clamping of its own.
public sealed record TransitionDetectionOptions
{
    public required FrameSamplingOptions SamplingOptions { get; init; }

    public required TransitionDetectionProfile Profile { get; init; }

    public static TransitionDetectionOptions ForProfile(
        AnalysisProfile analysisProfile,
        TransitionDetectionProfileVersion detectionProfileVersion = TransitionDetectionProfileVersion.V1) =>
        new()
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(analysisProfile),
            Profile = TransitionDetectionProfiles.GetDefaults(detectionProfileVersion),
        };
}
