namespace SceneForge.Accuracy.Fixtures;

// One label per row of the accuracy/benchmark report. The first 7 values
// mirror SceneForge.Media.Detection.TransitionType exactly (one group per
// real transition, ground truth = "a transition of this type is expected in
// window [Start, End]"). The remaining groups carry zero expected
// transitions each - they exist purely to measure false positives against
// content that must NOT be mistaken for a transition (distractors), and to
// prove the core hard-cut signature still resolves correctly under input-
// format quirks the analysis pipeline must tolerate (format-robustness
// variants).
public enum FixtureGroup
{
    HardCut,
    FadeToBlack,
    FadeFromBlack,
    Dissolve,
    Flash,
    BlurTransition,
    ZoomTransition,
    DirectionalSwipe,

    // Distractors: real, plausible non-transition content. Any detection
    // reported against one of these fixtures is, by construction, a false
    // positive.
    BlackHold,
    FrozenFrame,
    StaticShot,
    RapidMotion,

    // Format-robustness: the same known hard-cut signature, rebuilt under
    // an input-format quirk the pipeline must still handle correctly.
    VariableFrameRate,
    MixedResolution,
    Rotated,
}
