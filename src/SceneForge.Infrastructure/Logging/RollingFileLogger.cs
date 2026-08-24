namespace SceneForge.Infrastructure.Logging;

// A local, offline log file (CLAUDE.md rule 2 - no telemetry/network
// dependency) that never grows unbounded (rule 6): the active file rotates
// out to a timestamped name once it crosses MaxFileSizeBytes, and only the
// MaxRetainedFiles most recently modified rotated files are kept - anything
// older is deleted on the next rotation.
public sealed class RollingFileLogger : IAppLogger
{
    private const string RotatedFilePrefix = "sceneforge-";
    private const string RotatedFileSearchPattern = "sceneforge-*.log";

    private readonly object _lock = new();
    private readonly string _directory;
    private readonly string _currentFilePath;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxRetainedFiles;

    public RollingFileLogger(string directory, long maxFileSizeBytes = 5 * 1024 * 1024, int maxRetainedFiles = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (maxFileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileSizeBytes), maxFileSizeBytes, "Max file size must be positive.");
        }

        if (maxRetainedFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetainedFiles), maxRetainedFiles, "Max retained files must be positive.");
        }

        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
        _maxFileSizeBytes = maxFileSizeBytes;
        _maxRetainedFiles = maxRetainedFiles;
        _currentFilePath = Path.Combine(_directory, "sceneforge.log");
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var line = FormattableString.Invariant(
            $"{DateTimeOffset.UtcNow:O} [{level}] {message}{(exception is null ? string.Empty : $" -- {exception}")}");

        lock (_lock)
        {
            RotateIfNeeded();
            File.AppendAllLines(_currentFilePath, [line]);
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_currentFilePath);
        if (!info.Exists || info.Length < _maxFileSizeBytes)
        {
            return;
        }

        // A timestamp alone (even at millisecond precision) can collide when
        // rotations happen back-to-back faster than the clock advances; an
        // appended random suffix guarantees a unique target regardless of
        // timing.
        var rotatedPath = Path.Combine(
            _directory,
            $"{RotatedFilePrefix}{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.log");
        File.Move(_currentFilePath, rotatedPath);

        TrimOldFiles();
    }

    private void TrimOldFiles()
    {
        var rotatedFiles = Directory.EnumerateFiles(_directory, RotatedFileSearchPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        foreach (var stale in rotatedFiles.Skip(_maxRetainedFiles))
        {
            try
            {
                stale.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort trim; a locked rotated file is left for the
                // next rotation pass to retry.
            }
        }
    }
}
