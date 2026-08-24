using SceneForge.Media.Detection;
using SceneForge.Media.Domain;

namespace SceneForge.Accuracy.Fixtures;

// One classifier's worth of ground truth within a generated clip: the type
// it should be detected as, and the exact [Start, End] window ffmpeg was
// told to place that transition at (not a single point - a transition
// interval is never collapsed to a boundary point; see
// TransitionDetection's own remarks on this).
public sealed record ExpectedTransition(TransitionType Type, TimeRange Window);
