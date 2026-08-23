using SceneForge.Media.Detection.Signals;

namespace SceneForge.Media.Detection.Classification;

// Bounded sliding window of FrameSignalSample, capacity derived from
// profile.MaxTransitionDuration - classifiers never see more than one
// max-transition-duration's worth of signal history, which is what keeps
// TransitionDetector's memory flat regardless of video length (CLAUDE.md
// rule 6/7), independent of how many samples have streamed through so far.
// A plain List with front-eviction is intentionally simple: capacity is
// small (typically tens of samples - a few seconds at a few frames per
// second), so O(n) eviction is negligible and a true ring buffer would add
// complexity for no measurable benefit at this scale.
internal sealed class ClassifierWindow
{
    private readonly List<FrameSignalSample> _samples = [];
    private readonly TimeSpan _maxSpan;

    public ClassifierWindow(TimeSpan maxSpan)
    {
        _maxSpan = maxSpan;
    }

    public IReadOnlyList<FrameSignalSample> Samples => _samples;

    public void Append(FrameSignalSample sample)
    {
        _samples.Add(sample);

        while (_samples.Count > 1 && (sample.Timestamp - _samples[0].PreviousTimestamp) > _maxSpan)
        {
            _samples.RemoveAt(0);
        }
    }
}
