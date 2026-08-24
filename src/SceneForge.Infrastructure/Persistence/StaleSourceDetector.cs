namespace SceneForge.Infrastructure.Persistence;

public sealed class StaleSourceDetector : IStaleSourceDetector
{
    public SourceFingerprint Capture(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"Cannot fingerprint '{fullPath}': the file does not exist.", fullPath);
        }

        return new SourceFingerprint
        {
            FilePath = fullPath,
            SizeBytes = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc,
        };
    }

    public SourceFreshnessResult CheckFreshness(SourceFingerprint recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        var info = new FileInfo(recorded.FilePath);
        if (!info.Exists)
        {
            return new SourceFreshnessResult
            {
                Status = SourceFreshnessStatus.Missing,
                Message = $"Source file '{recorded.FilePath}' can no longer be found.",
            };
        }

        if (info.Length != recorded.SizeBytes || info.LastWriteTimeUtc != recorded.LastWriteTimeUtc)
        {
            return new SourceFreshnessResult
            {
                Status = SourceFreshnessStatus.Changed,
                Message = $"Source file '{recorded.FilePath}' has changed on disk since this project was last saved (size or last-modified time differs).",
            };
        }

        return new SourceFreshnessResult
        {
            Status = SourceFreshnessStatus.Fresh,
            Message = "Source file matches the fingerprint recorded when this project was last saved.",
        };
    }
}
