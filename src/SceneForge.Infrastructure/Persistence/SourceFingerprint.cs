namespace SceneForge.Infrastructure.Persistence;

// The identity check a persisted project uses to decide whether a source
// file on disk is still the same file it was analyzed from - deliberately
// cheap (size + last-write-time, no content hash/full read of a
// potentially large video file) and deliberately not a full-content guarantee
// (CLAUDE.md rule 10: never claim a stronger check than what was actually
// performed). See IStaleSourceDetector for how this is compared against the
// file's current state.
public sealed record SourceFingerprint
{
    public required string FilePath { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTimeOffset LastWriteTimeUtc { get; init; }
}
