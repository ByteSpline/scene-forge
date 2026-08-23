namespace SceneForge.Media.Detection.Signals;

// Signed change in mean grayscale intensity (0..1 scale each), negative
// meaning the frame got darker. The sign - not just the magnitude - is what
// lets FadeBlackClassifier tell a fade-to-black apart from a fade-from-black.
internal sealed class LuminanceDeltaExtractor : IPairSignalExtractor
{
    public string Name => nameof(LuminanceDeltaExtractor);

    public double Extract(AnalyzedFrame previous, AnalyzedFrame current) =>
        current.MeanLuminance - previous.MeanLuminance;
}
