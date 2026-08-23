namespace SceneForge.Media.Detection.Fusion;

// Every numeric threshold that drives fusion/classification lives behind a
// version so a detection result is always reproducible and auditable: "V1
// flagged this as a Dissolve" is a falsifiable, re-runnable claim, not a
// magic number nobody can trace. New tuning becomes a new version value
// (e.g. V2) rather than silently mutating V1's meaning out from under
// anything that already recorded "detected with V1".
public enum TransitionDetectionProfileVersion
{
    V1,
}
