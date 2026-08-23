namespace SceneForge.Media.Detection.Signals;

// Signed change in Laplacian variance (a standard focus/sharpness measure -
// higher variance means sharper). Unbounded, unlike the 0..1 signals:
// classifiers reason about it as a ratio against a window baseline, not an
// absolute value, since "sharp" varies hugely by source content.
internal sealed class LaplacianBlurChangeExtractor : IPairSignalExtractor
{
    public string Name => nameof(LaplacianBlurChangeExtractor);

    public double Extract(AnalyzedFrame previous, AnalyzedFrame current) =>
        current.LaplacianVariance - previous.LaplacianVariance;
}
