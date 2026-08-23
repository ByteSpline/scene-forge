namespace SceneForge.Media.Detection;

// The public result of transition analysis. IMPORTANT (this is not
// incidental API shape - it is a hard project rule): [Start, End] is the
// full, potentially-contaminated transition interval - the span of source
// frames a downstream splitting/export stage must NOT treat as clean,
// undamaged footage of either neighboring scene. BoundaryTimestamp is a
// single recommended splice point *within* that interval, offered only for
// callers that need exactly one cut point (e.g. a simple scene list) - it
// is never a substitute for [Start, End] and must never be used as if it
// captured the width of the contamination. A scene boundary is a point; a
// transition interval is a range. Treating them as equivalent is exactly
// the bug this type exists to prevent.
public sealed record TransitionDetection
{
    public required TransitionType Type { get; init; }

    // Start of the contaminated interval - the last moment content can
    // still be trusted as belonging cleanly to the preceding scene.
    public required TimeSpan Start { get; init; }

    // Timestamp of strongest evidence for this transition (e.g. the
    // midpoint of a dissolve, the fully-black frame of a fade-to-black).
    public required TimeSpan Peak { get; init; }

    // End of the contaminated interval - the first moment content can be
    // trusted as belonging cleanly to the following scene.
    public required TimeSpan End { get; init; }

    // A single recommended splice point within [Start, End]. See the type
    // remarks above: this is not equivalent to, and must not replace, the
    // [Start, End] interval.
    public required TimeSpan BoundaryTimestamp { get; init; }

    // 0..1, never claimed as absolute certainty (CLAUDE.md rule 10).
    public required double Confidence { get; init; }

    // Named signal contributions (e.g. "HsvHistogramDistance" -> 0.82) that
    // fed the classifier decision, for diagnostics and export - never
    // opaque.
    public required IReadOnlyDictionary<string, double> ContributingSignals { get; init; }

    // Human-readable explanation of why this interval was classified as
    // Type, referencing the contributing signals' behavior.
    public required string DiagnosticReason { get; init; }

    public TimeSpan Duration => End - Start;
}
