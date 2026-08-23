namespace SceneForge.Media.Detection.Signals;

// The one required signal that is a property of a single frame, not a
// delta between two - AnalyzedFrame.Create already computes both scores
// (fraction of near-black / near-white pixels) up front, so these
// extractors are thin, testable accessors rather than doing their own Mat
// work, keeping every extractor's *provenance* explicit (FadeBlackClassifier
// and FlashClassifier both read these, for different reasons, and both
// need to be able to point at exactly which extractor produced the value).
internal sealed class BlackScoreExtractor : ISingleFrameSignalExtractor
{
    public string Name => nameof(BlackScoreExtractor);

    public double Extract(AnalyzedFrame frame) => frame.BlackScore;
}

internal sealed class WhiteScoreExtractor : ISingleFrameSignalExtractor
{
    public string Name => nameof(WhiteScoreExtractor);

    public double Extract(AnalyzedFrame frame) => frame.WhiteScore;
}
