using SceneForge.Media.Tooling;

namespace SceneForge.App.Tests.TestSupport;

internal sealed class FakeNativeDependencyDiagnosticsService : INativeDependencyDiagnosticsService
{
    private readonly Queue<NativeDependencyDiagnosticsReport> _reports;

    public int CallCount { get; private set; }

    public FakeNativeDependencyDiagnosticsService(params NativeDependencyDiagnosticsReport[] reports)
    {
        _reports = new Queue<NativeDependencyDiagnosticsReport>(reports);
    }

    public static NativeDependencyDiagnosticsReport Passing() => new()
    {
        Results =
        [
            new NativeComponentCheckResult { ComponentName = "FFmpeg / FFprobe", IsAvailable = true, Detail = "ok" },
            new NativeComponentCheckResult { ComponentName = "Visual C++ Runtime", IsAvailable = true, Detail = "ok" },
            new NativeComponentCheckResult { ComponentName = "OpenCV native library", IsAvailable = true, Detail = "ok" },
        ],
    };

    public static NativeDependencyDiagnosticsReport Failing() => new()
    {
        Results =
        [
            new NativeComponentCheckResult { ComponentName = "FFmpeg / FFprobe", IsAvailable = false, Detail = "missing", RemediationGuidance = "reinstall" },
            new NativeComponentCheckResult { ComponentName = "Visual C++ Runtime", IsAvailable = true, Detail = "ok" },
            new NativeComponentCheckResult { ComponentName = "OpenCV native library", IsAvailable = true, Detail = "ok" },
        ],
    };

    public Task<NativeDependencyDiagnosticsReport> RunAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        var report = _reports.Count > 1 ? _reports.Dequeue() : _reports.Peek();
        return Task.FromResult(report);
    }
}
