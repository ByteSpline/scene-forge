namespace SceneForge.Media.Sampling;

// Named presets over FrameSamplingOptions; see FrameSamplingProfiles for the
// documented default values each maps to. Callers are never limited to these
// three - FrameSamplingOptions can always be constructed directly with
// custom values.
public enum AnalysisProfile
{
    Fast,
    Balanced,
    Accurate,
}
