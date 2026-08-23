namespace SceneForge.Media.Detection.Fusion;

// Documented default TransitionDetectionProfile for each
// TransitionDetectionProfileVersion. Same pattern as
// Sampling.FrameSamplingProfiles: this is the only place a version's
// numbers live, and every value remains overridable via `with { ... }` on
// the returned profile. V1's thresholds were tuned against the synthetic
// fixture matrix in tests/SceneForge.Media.Tests/Detection/Fixtures - see
// docs/PHASE_06_REPORT.md for the measured precision/recall/boundary-error
// evidence behind these specific numbers.
public static class TransitionDetectionProfiles
{
    public static TransitionDetectionProfile GetDefaults(TransitionDetectionProfileVersion version) => version switch
    {
        TransitionDetectionProfileVersion.V1 => new TransitionDetectionProfile
        {
            Version = TransitionDetectionProfileVersion.V1,
            MaxTransitionDuration = TimeSpan.FromSeconds(2.5),
            MergeGapTolerance = TimeSpan.FromMilliseconds(150),
            PreBufferDuration = TimeSpan.FromMilliseconds(100),
            PostBufferDuration = TimeSpan.FromMilliseconds(100),
            HardCut = new HardCutThresholds(),
            FadeBlack = new FadeBlackThresholds(),
            Dissolve = new DissolveThresholds(),
            Flash = new FlashThresholds(),
            BlurTransition = new BlurTransitionThresholds(),
            ZoomTransition = new ZoomTransitionThresholds(),
            DirectionalSwipe = new DirectionalSwipeThresholds(),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown transition detection profile version."),
    };
}
