using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Detection.Classification;

// One classifier per TransitionType - no shared "figure out what kind of
// transition this is" god-function. Each classifier scans the given window
// independently and returns zero or more candidates; TransitionFuser (not
// the classifier) is responsible for resolving overlaps between classifiers
// or between repeated calls as the window slides. A classifier must never
// look outside the given window - that boundary is what keeps the whole
// pipeline's memory bounded regardless of video length.
internal interface ITransitionClassifier
{
    // Nominal identity for logging/registration - most classifiers only
    // ever emit candidates of this type, but FadeBlackClassifier emits both
    // FadeToBlack and FadeFromBlack candidates (see its own remarks);
    // callers must read Type from each returned TransitionCandidate, never
    // assume it matches this property.
    TransitionType Type { get; }

    IReadOnlyList<TransitionCandidate> Classify(IReadOnlyList<FrameSignalSample> window, TransitionDetectionProfile profile);
}
