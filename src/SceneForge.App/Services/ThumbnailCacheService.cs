using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using SceneForge.Core.Resources;
using SceneForge.Media.Processes;
using SceneForge.Media.Tooling;

namespace SceneForge.App.Services;

// Generates thumbnails via a single-frame ffmpeg extraction (CLAUDE.md rule
// 3: the media stack is FFmpeg/FFprobe/OpenCvSharp) into a disk cache under
// %LOCALAPPDATA%\SceneForge\ThumbnailCache, keyed by the source file's own
// path/size/last-write-time plus the requested timestamp - so a cache entry
// is never served for a source file that has since changed on disk.
// Concurrency is capped via IAdaptiveResourceGovernor.MaxWorkers (never one
// ffmpeg process per row spawned at once, and never more concurrent
// processes than this machine has spare logical CPUs for) and the cache
// directory itself is swept back to a target size whenever it grows past a
// hard cap, so neither concurrent process count nor disk usage is unbounded
// (CLAUDE.md rule 6/7).
public sealed class ThumbnailCacheService : IThumbnailCacheService, IDisposable
{
    private const int ThumbnailWidthPixels = 160;
    private const int MaxCachedThumbnailFiles = 4000;
    private const int SweepTargetFileCount = 3000;

    private readonly IFfmpegToolLocator _toolLocator;
    private readonly IProcessRunner _processRunner;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public ThumbnailCacheService(IFfmpegToolLocator toolLocator, IProcessRunner processRunner, IAdaptiveResourceGovernor resourceGovernor)
    {
        _toolLocator = toolLocator;
        _processRunner = processRunner;
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SceneForge",
            "ThumbnailCache");
        Directory.CreateDirectory(_cacheDirectory);

        var maxConcurrentGenerations = resourceGovernor.MaxWorkers;
        _concurrencyLimiter = new SemaphoreSlim(maxConcurrentGenerations, maxConcurrentGenerations);
    }

    public async Task<BitmapSource?> GetThumbnailAsync(string sourceVideoPath, TimeSpan timestamp, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourceVideoPath))
        {
            return null;
        }

        var cachePath = BuildCachePath(sourceVideoPath, timestamp);
        if (File.Exists(cachePath))
        {
            return LoadFrozenBitmap(cachePath);
        }

        await _concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(cachePath))
            {
                return LoadFrozenBitmap(cachePath);
            }

            if (!await TryGenerateAsync(sourceVideoPath, timestamp, cachePath, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            EnforceCacheBound();
            return LoadFrozenBitmap(cachePath);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    public void Dispose() => _concurrencyLimiter.Dispose();

    private async Task<bool> TryGenerateAsync(string sourceVideoPath, TimeSpan timestamp, string cachePath, CancellationToken cancellationToken)
    {
        // Must still end in ".jpg" - ffmpeg infers its output muxer from the
        // output path's extension, and a ".tmp-<guid>" suffix with no
        // recognized extension fails with "Unable to choose an output
        // format" before any frame is ever written.
        var tempPath = Path.Combine(Path.GetDirectoryName(cachePath)!, $"{Path.GetFileNameWithoutExtension(cachePath)}.tmp-{Guid.NewGuid():N}.jpg");
        try
        {
            var tools = await _toolLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
            var result = await _processRunner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = tools.FfmpegPath,
                    Arguments =
                    [
                        "-y",
                        "-ss",
                        timestamp.TotalSeconds.ToString(CultureInfo.InvariantCulture),
                        "-i",
                        sourceVideoPath,
                        "-frames:v",
                        "1",
                        "-vf",
                        $"scale={ThumbnailWidthPixels}:-2",
                        "-q:v",
                        "4",
                        tempPath,
                    ],
                    Timeout = TimeSpan.FromSeconds(15),
                },
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0 || !File.Exists(tempPath))
            {
                return false;
            }

            File.Move(tempPath, cachePath, overwrite: true);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Thumbnail generation is best-effort UI polish - a missing
            // thumbnail degrades gracefully to a placeholder, never blocks
            // the review workflow.
            return false;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private string BuildCachePath(string sourceVideoPath, TimeSpan timestamp)
    {
        var fullPath = Path.GetFullPath(sourceVideoPath);
        string signature;
        try
        {
            var info = new FileInfo(fullPath);
            signature = $"{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{timestamp.Ticks}|{ThumbnailWidthPixels}";
        }
        catch (IOException)
        {
            signature = $"{fullPath}|{timestamp.Ticks}|{ThumbnailWidthPixels}";
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
        return Path.Combine(_cacheDirectory, $"{hash}.jpg");
    }

    private void EnforceCacheBound()
    {
        var files = new DirectoryInfo(_cacheDirectory).GetFiles("*.jpg");
        if (files.Length <= MaxCachedThumbnailFiles)
        {
            return;
        }

        var oldestFirst = files.OrderBy(f => f.LastWriteTimeUtc).ToArray();
        var deleteCount = files.Length - SweepTargetFileCount;
        for (var i = 0; i < deleteCount && i < oldestFirst.Length; i++)
        {
            try
            {
                oldestFirst[i].Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static BitmapImage? LoadFrozenBitmap(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
