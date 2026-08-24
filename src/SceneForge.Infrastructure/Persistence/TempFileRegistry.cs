using System.Text.Json;

namespace SceneForge.Infrastructure.Persistence;

public sealed class TempFileRegistry : ITempFileRegistry
{
    private const string ManifestFileName = "registry.json";

    private readonly object _lock = new();
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _manifestPath;
    private readonly string _rootWithSeparator;

    public string RootDirectory { get; }

    public TempFileRegistry(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        _rootWithSeparator = RootDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? RootDirectory
            : RootDirectory + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(RootDirectory);
        _manifestPath = Path.Combine(RootDirectory, ManifestFileName);

        LoadManifest();
    }

    public IReadOnlyList<string> RegisteredFiles
    {
        get
        {
            lock (_lock)
            {
                return [.. _files];
            }
        }
    }

    public void Register(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        EnsureWithinRoot(fullPath);

        lock (_lock)
        {
            _files.Add(fullPath);
            SaveManifest();
        }
    }

    public void Unregister(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);

        lock (_lock)
        {
            if (_files.Remove(fullPath))
            {
                SaveManifest();
            }
        }
    }

    public Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            foreach (var file in _files)
            {
                DeleteIfWithinRoot(file);
            }

            _files.Clear();
            SaveManifest();
        }

        return Task.CompletedTask;
    }

    public Task SweepOrphansAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var path in Directory.EnumerateFiles(RootDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.GetFullPath(path);
                if (string.Equals(fullPath, _manifestPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!_files.Contains(fullPath))
                {
                    DeleteIfWithinRoot(fullPath);
                }
            }
        }

        return Task.CompletedTask;
    }

    private void EnsureWithinRoot(string fullPath)
    {
        if (!fullPath.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{fullPath}' is outside the app-owned temp directory '{RootDirectory}' and cannot be registered for cleanup.");
        }
    }

    // Silently refuses (never throws) a path outside RootDirectory - unlike
    // Register, this runs during best-effort cleanup, where a caller-supplied
    // or manifest-loaded path should never be able to escalate into deleting
    // something outside the app's own temp area (CLAUDE.md rule 11).
    private void DeleteIfWithinRoot(string fullPath)
    {
        if (!fullPath.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a file still locked by another process is left for
            // the next cleanup pass to retry, never a fatal error.
        }
    }

    private void LoadManifest()
    {
        if (!File.Exists(_manifestPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_manifestPath);
            var entries = JsonSerializer.Deserialize<string[]>(json) ?? [];
            foreach (var entry in entries)
            {
                _files.Add(entry);
            }
        }
        catch (JsonException)
        {
            // A corrupted manifest is not fatal - starting with an empty
            // registry (rather than blocking startup) is the safer default;
            // SweepOrphansAsync will still find and clean up any real
            // leftover files under RootDirectory on this same startup pass.
        }
    }

    private void SaveManifest() => File.WriteAllText(_manifestPath, JsonSerializer.Serialize(_files.ToArray()));
}
