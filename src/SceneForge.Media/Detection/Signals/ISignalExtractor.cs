namespace SceneForge.Media.Detection.Signals;

// One signal, one extractor, each independently unit-tested - this is what
// "composable" means in practice: no classifier or fusion step is allowed
// to reach into Mat pixel data directly, only through these named,
// single-purpose contracts. Named (not just a lambda) so every
// FrameSignalSample field can be traced back to exactly one extractor.
internal interface IPairSignalExtractor
{
    string Name { get; }

    double Extract(AnalyzedFrame previous, AnalyzedFrame current);
}

// For the one required signal (black/white-frame score) that is a property
// of a single frame rather than a delta between two.
internal interface ISingleFrameSignalExtractor
{
    string Name { get; }

    double Extract(AnalyzedFrame frame);
}

// For the one required signal (global motion estimate) whose result is a
// small vector of related scalars rather than a single double.
internal interface IGlobalMotionSignalExtractor
{
    string Name { get; }

    GlobalMotionEstimate Extract(AnalyzedFrame previous, AnalyzedFrame current);
}
