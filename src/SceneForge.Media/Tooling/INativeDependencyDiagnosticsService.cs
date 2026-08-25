namespace SceneForge.Media.Tooling;

// First-run(-and-every-run) preflight, run before the app lets a user start
// an analysis: proves ffmpeg/ffprobe, the Visual C++ runtime, and the
// OpenCV native library are all actually present and usable, rather than
// letting a missing/broken native dependency surface later as an opaque
// mid-pipeline crash.
public interface INativeDependencyDiagnosticsService
{
    Task<NativeDependencyDiagnosticsReport> RunAsync(CancellationToken cancellationToken);
}
