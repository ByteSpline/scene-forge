namespace SceneForge.Media.Detection.Classification;

// Pre-fusion output of a single classifier - same shape as the public
// TransitionDetection minus BoundaryTimestamp, which only exists once
// TransitionFuser has resolved (possibly merged) candidates into final
// detections.
internal sealed record TransitionCandidate
{
    public required TransitionType Type { get; init; }

    public required TimeSpan Start { get; init; }

    public required TimeSpan Peak { get; init; }

    public required TimeSpan End { get; init; }

    public required double Confidence { get; init; }

    public required IReadOnlyDictionary<string, double> ContributingSignals { get; init; }

    public required string DiagnosticReason { get; init; }
}
