namespace SceneForge.Infrastructure.Persistence;

public enum SourceFreshnessStatus
{
    Fresh,
    Missing,
    Changed,
}

// Never a bare boolean (CLAUDE.md rule 10) - Missing and Changed are
// distinct, explainable outcomes with their own Message, not one opaque
// "stale" flag.
public sealed record SourceFreshnessResult
{
    public required SourceFreshnessStatus Status { get; init; }

    public required string Message { get; init; }

    public bool IsStale => Status != SourceFreshnessStatus.Fresh;
}
