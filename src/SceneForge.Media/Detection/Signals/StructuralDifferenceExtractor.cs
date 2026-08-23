using OpenCvSharp;

namespace SceneForge.Media.Detection.Signals;

// Normalized mean absolute grayscale pixel difference, 0..1. The most
// direct "how different do these two frames look overall" signal - the
// primary driver for HardCutClassifier and DissolveClassifier, which tell
// each other apart by how this value evolves across the window (a single
// spike vs. a sustained bell-shaped ramp), not by its magnitude alone.
internal sealed class StructuralDifferenceExtractor : IPairSignalExtractor
{
    public string Name => nameof(StructuralDifferenceExtractor);

    public double Extract(AnalyzedFrame previous, AnalyzedFrame current)
    {
        using var diff = new Mat();
        Cv2.Absdiff(previous.Gray, current.Gray, diff);
        return Cv2.Mean(diff).Val0 / 255.0;
    }
}
