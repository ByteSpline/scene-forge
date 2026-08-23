namespace SceneForge.Media.Detection.Signals;

// Signed change in the fraction of Canny-detected edge pixels. A blur
// transition loses fine edges (negative delta then recovers positive);
// a hard cut between two differently-detailed scenes can also show a large
// delta, which is why BlurTransitionClassifier correlates this with
// LaplacianBlurChange rather than reading it alone.
internal sealed class EdgeDensityDeltaExtractor : IPairSignalExtractor
{
    public string Name => nameof(EdgeDensityDeltaExtractor);

    public double Extract(AnalyzedFrame previous, AnalyzedFrame current) =>
        current.EdgeDensity - previous.EdgeDensity;
}
