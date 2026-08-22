namespace SceneForge.Media.Sampling;

// Documented default values for each AnalysisProfile. These are the only
// place the numbers live; FrameSamplingOptions.ForProfile and every caller
// that wants "the Fast/Balanced/Accurate preset" should go through
// GetDefaults rather than repeating literals.
//
//   Fast:     320 px analysis width, 2 fps  - cheapest, coarsest sampling.
//   Balanced: 384 px analysis width, 4 fps  - default trade-off.
//   Accurate: 480 px analysis width, 8 fps, plus expensive per-frame
//             signals enabled for later analysis stages.
//
// All three remain fully overridable: FrameSamplingOptions.ForProfile(...)
// returns a record that callers can further customize with `with { ... }`.
public static class FrameSamplingProfiles
{
    public const int FastAnalysisWidthPixels = 320;
    public const double FastSampleFramesPerSecond = 2.0;

    public const int BalancedAnalysisWidthPixels = 384;
    public const double BalancedSampleFramesPerSecond = 4.0;

    public const int AccurateAnalysisWidthPixels = 480;
    public const double AccurateSampleFramesPerSecond = 8.0;

    public static FrameSamplingOptions GetDefaults(AnalysisProfile profile) => profile switch
    {
        AnalysisProfile.Fast => new FrameSamplingOptions
        {
            AnalysisWidthPixels = FastAnalysisWidthPixels,
            SampleFramesPerSecond = FastSampleFramesPerSecond,
            IncludeExpensiveSignals = false,
        },
        AnalysisProfile.Balanced => new FrameSamplingOptions
        {
            AnalysisWidthPixels = BalancedAnalysisWidthPixels,
            SampleFramesPerSecond = BalancedSampleFramesPerSecond,
            IncludeExpensiveSignals = false,
        },
        AnalysisProfile.Accurate => new FrameSamplingOptions
        {
            AnalysisWidthPixels = AccurateAnalysisWidthPixels,
            SampleFramesPerSecond = AccurateSampleFramesPerSecond,
            IncludeExpensiveSignals = true,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown analysis profile."),
    };
}
