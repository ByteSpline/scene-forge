namespace SceneForge.Media.Sampling;

// Every knob a caller can configure for a sampling run. Values are clamped
// to a safe range in the init setter (same pattern as
// ProcessExecutionRequest.MaxCapturedBytesPerStream) so a misconfigured
// caller can never turn this into unbounded memory/CPU use rather than
// throwing at construction time.
public sealed record FrameSamplingOptions
{
    private const int MinAnalysisWidthPixels = 16;
    private const int MaxAnalysisWidthPixels = 4096;

    private const double MinSampleFramesPerSecond = 0.1;
    private const double MaxSampleFramesPerSecond = 60.0;

    // Bounds how many decoded-but-not-yet-consumed frames may sit in the
    // producer/consumer channel at once (CLAUDE.md rule 6: bounded
    // concurrency/memory). A slow consumer stalls the ffmpeg-reading
    // producer once this many frames are buffered, rather than growing
    // memory without limit.
    private const int MinChannelCapacity = 1;
    private const int MaxChannelCapacity = 64;

    private readonly int _analysisWidthPixels = FrameSamplingProfiles.BalancedAnalysisWidthPixels;
    private readonly double _sampleFramesPerSecond = FrameSamplingProfiles.BalancedSampleFramesPerSecond;
    private readonly int _channelCapacity = 4;

    // Target output frame width in pixels; height is derived from the
    // source's aspect ratio and rounded to an even number of pixels.
    public int AnalysisWidthPixels
    {
        get => _analysisWidthPixels;
        init => _analysisWidthPixels = Math.Clamp(value, MinAnalysisWidthPixels, MaxAnalysisWidthPixels);
    }

    // Target sample rate. Frames are chosen by ffmpeg's fps filter, which
    // selects the nearest source frame for each output tick rather than
    // synthesizing intermediate frames, so every emitted FrameSample carries
    // a genuine source timestamp.
    public double SampleFramesPerSecond
    {
        get => _sampleFramesPerSecond;
        init => _sampleFramesPerSecond = Math.Clamp(value, MinSampleFramesPerSecond, MaxSampleFramesPerSecond);
    }

    public FrameSamplePixelFormat PixelFormat { get; init; } = FrameSamplePixelFormat.Bgr24;

    public int ChannelCapacity
    {
        get => _channelCapacity;
        init => _channelCapacity = Math.Clamp(value, MinChannelCapacity, MaxChannelCapacity);
    }

    // Flags that expensive per-frame signals (e.g. histograms, edge maps)
    // should be computed by later analysis stages that consume these
    // samples. This phase only carries the flag through - it does not
    // compute any signal itself.
    public bool IncludeExpensiveSignals { get; init; }

    public static FrameSamplingOptions ForProfile(AnalysisProfile profile) => FrameSamplingProfiles.GetDefaults(profile);
}
