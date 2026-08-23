namespace SceneForge.Media.Detection;

// One transition type per classifier (see Detection/Classification/). Each
// value corresponds to exactly one ITransitionClassifier implementation -
// there is no catch-all "unknown transition" value, so every detection is
// traceable to the specific classifier and signal signature that produced
// it.
public enum TransitionType
{
    HardCut,
    FadeToBlack,
    FadeFromBlack,
    Dissolve,
    Flash,
    BlurTransition,
    ZoomTransition,
    DirectionalSwipe,
}
