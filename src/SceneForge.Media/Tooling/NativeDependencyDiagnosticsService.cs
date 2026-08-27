namespace SceneForge.Media.Tooling;

// Runs the same three checks on every launch rather than gating only a
// literal "first run": a first-run-only flag would go stale the moment
// antivirus quarantines a file under 'tools\', a Windows update breaks the
// Visual C++ runtime, or a user copies the app folder without the tools
// subfolders - and silently re-enabling a now-broken app is worse than the
// negligible cost of re-running a handful of fast, local, no-network checks
// on every startup.
public sealed class NativeDependencyDiagnosticsService : INativeDependencyDiagnosticsService
{
    // The three DLLs a Visual C++ 2015-2022 x64 native component (including
    // OpenCvSharpExtern.dll) depends on; missing any one of them causes the
    // exact same native load failure OpenCV's own check below would report,
    // so checking these first lets the diagnostics distinguish "install the
    // VC++ redistributable" from "the tools\opencv folder is missing/corrupt".
    private static readonly string[] VcRuntimeLibraries = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll"];

    private readonly IFfmpegToolLocator _ffmpegToolLocator;
    private readonly IOpenCvNativeProbe _openCvProbe;
    private readonly INativeLibraryProbe _nativeLibraryProbe;

    public NativeDependencyDiagnosticsService(
        IFfmpegToolLocator ffmpegToolLocator,
        IOpenCvNativeProbe openCvProbe,
        INativeLibraryProbe nativeLibraryProbe)
    {
        _ffmpegToolLocator = ffmpegToolLocator;
        _openCvProbe = openCvProbe;
        _nativeLibraryProbe = nativeLibraryProbe;
    }

    public async Task<NativeDependencyDiagnosticsReport> RunAsync(CancellationToken cancellationToken)
    {
        var results = new List<NativeComponentCheckResult>
        {
            await CheckFfmpegAsync(cancellationToken).ConfigureAwait(false),
            CheckVcRuntime(),
            CheckOpenCv(),
        };

        return new NativeDependencyDiagnosticsReport { Results = results };
    }

    private async Task<NativeComponentCheckResult> CheckFfmpegAsync(CancellationToken cancellationToken)
    {
        try
        {
            var paths = await _ffmpegToolLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
            return new NativeComponentCheckResult
            {
                ComponentName = "FFmpeg / FFprobe",
                IsAvailable = true,
                Detail = $"Found and launched successfully from '{Path.GetDirectoryName(paths.FfmpegPath)}'.",
            };
        }
        catch (Exception ex) when (ex is FfmpegToolsNotFoundException or FfmpegToolsIncompatibleException)
        {
            return new NativeComponentCheckResult
            {
                ComponentName = "FFmpeg / FFprobe",
                IsAvailable = false,
                Detail = ex.Message,
                RemediationGuidance = "Reinstall SceneForge, or restore the 'tools\\ffmpeg' folder next to SceneForge.exe with ffmpeg.exe and ffprobe.exe in it.",
            };
        }
    }

    private NativeComponentCheckResult CheckVcRuntime()
    {
        var missing = VcRuntimeLibraries.Where(library => !_nativeLibraryProbe.IsLoadable(library)).ToList();

        return missing.Count == 0
            ? new NativeComponentCheckResult
            {
                ComponentName = "Visual C++ Runtime",
                IsAvailable = true,
                Detail = "vcruntime140.dll, vcruntime140_1.dll, and msvcp140.dll all loaded successfully.",
            }
            : new NativeComponentCheckResult
            {
                ComponentName = "Visual C++ Runtime",
                IsAvailable = false,
                Detail = $"Could not load: {string.Join(", ", missing)}.",
                RemediationGuidance = "Install the Microsoft Visual C++ x64 Redistributable (vc_redist.x64.exe) and restart SceneForge.",
            };
    }

    private NativeComponentCheckResult CheckOpenCv()
    {
        try
        {
            var buildInformation = _openCvProbe.Probe();
            var firstLine = buildInformation
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "OpenCV native library loaded.";

            return new NativeComponentCheckResult
            {
                ComponentName = "OpenCV native library",
                IsAvailable = true,
                Detail = firstLine,
            };
        }
        catch (Exception ex)
        {
            // Deliberately broad: this probe's entire purpose is to prove
            // whether the native call works at all, so any failure mode
            // (DllNotFoundException, BadImageFormatException,
            // EntryPointNotFoundException, a raw SEHException, ...) must be
            // caught and reported as a diagnostic result, never left to
            // propagate as an unhandled exception out of a startup check.
            return new NativeComponentCheckResult
            {
                ComponentName = "OpenCV native library",
                IsAvailable = false,
                Detail = ex.Message,
                RemediationGuidance = "Reinstall SceneForge, or restore the 'tools\\opencv' folder next to SceneForge.exe with OpenCvSharpExtern.dll in it. If the Visual C++ Runtime check above also failed, fix that first.",
            };
        }
    }
}
