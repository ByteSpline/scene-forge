namespace SceneForge.Media.Extraction;

// One factor's pass/fail verdict plus the measured value behind it - the
// per-clip analogue of TransitionDetection's ContributingSignals/
// DiagnosticReason: acceptance and rejection are always explainable, never
// a single opaque boolean. Reasons.Count is always 8 regardless of
// Accepted, so a caller inspecting a rejected candidate can see every
// factor's individual verdict, not just the one that happened to trip
// first.
public sealed record ScoreReason
{
    // Name of the scored factor, e.g. "Sharpness", "FreezeRisk" - matches
    // the corresponding ClipScore property name, or "Overall" for the final
    // weighted-aggregate verdict.
    public required string Factor { get; init; }

    public required bool Passed { get; init; }

    // Null when Passed is true; set to the specific reason when false.
    public required RejectionReason? Code { get; init; }

    // Human-readable detail including the measured value, e.g. "mean
    // Laplacian variance 12.4 below reference 50.0".
    public required string Detail { get; init; }
}

// Every candidate's scored factors, each normalized 0..1, plus the reasons
// that explain Accepted either way (see ScoreReason - always 8 entries: one
// per factor below, plus a final "Overall" entry). See ClipScorer for how
// each factor is computed and
// CleanClipExtractionOptions.CleanClipScoringOptions for the
// thresholds/weights driving Accepted - all deliberately heuristic,
// unmeasured-against-real-footage constants (CLAUDE.md rule 10: never
// claimed as calibrated or absolute).
public sealed record ClipScore
{
    // How close the candidate's duration sits to the configured maximum
    // within [MinClipDuration, MaxClipDuration]; higher is better.
    public required double Duration { get; init; }

    // Mean Laplacian-variance focus measure across the candidate's sampled
    // frames, normalized against a reference value; higher is sharper.
    public required double Sharpness { get; init; }

    // Inverse of mean frame-to-frame structural difference across the
    // candidate; higher means visually calmer/more stable content.
    public required double Stability { get; init; }

    // How well-exposed the candidate is (mid-range luminance, low black/
    // white pixel fraction); higher is better exposed.
    public required double Exposure { get; init; }

    // Fraction of frame-to-frame gaps that were suspiciously near-identical
    // (below FreezeNearZeroThreshold), suggesting a stuck/frozen source
    // frame rather than genuinely calm content. HIGHER IS WORSE - this is a
    // risk score, not a quality score, unlike every other factor here.
    public required double FreezeRisk { get; init; }

    // Normalized distance from the candidate's nearest excluded interval
    // (transition/black-frozen-invalid/user-excluded); higher means farther
    // from contaminated footage, so safer.
    public required double TransitionDistance { get; init; }

    // How much more the frame's border regions (caption/logo-prone) show
    // edges than its interior, normalized; HIGHER IS WORSE - suggests a
    // static overlay (lower-third, watermark) rather than genuine content
    // detail.
    public required double OverlaySuspicion { get; init; }

    // Weighted aggregate of the seven factors above (FreezeRisk and
    // OverlaySuspicion inverted first, since higher-is-worse for those
    // two), 0..1.
    public required double Overall { get; init; }

    public required bool Accepted { get; init; }

    public required IReadOnlyList<ScoreReason> Reasons { get; init; }
}
