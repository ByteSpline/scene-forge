using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Tooling;

namespace SceneForge.Media.Tests.Tooling;

public sealed class NativeDependencyDiagnosticsServiceTests
{
    [Fact]
    public async Task RunAsync_EverythingAvailable_ReportsAllPassed()
    {
        var service = new NativeDependencyDiagnosticsService(
            new FakeFfmpegToolLocator(),
            FakeOpenCvNativeProbe.Succeeding(),
            new FakeNativeLibraryProbe());

        var report = await service.RunAsync(CancellationToken.None);

        Assert.True(report.AllPassed);
        Assert.Equal(3, report.Results.Count);
        Assert.All(report.Results, r => Assert.True(r.IsAvailable));
        Assert.All(report.Results, r => Assert.Null(r.RemediationGuidance));
    }

    [Fact]
    public async Task RunAsync_FfmpegMissing_ReportsFfmpegFailureWithRemediation()
    {
        var service = new NativeDependencyDiagnosticsService(
            new FakeFfmpegToolLocator(exceptionToThrow: new FfmpegToolsNotFoundException(["C:\\app\\tools\\ffmpeg\\ffmpeg.exe"])),
            FakeOpenCvNativeProbe.Succeeding(),
            new FakeNativeLibraryProbe());

        var report = await service.RunAsync(CancellationToken.None);

        Assert.False(report.AllPassed);
        var ffmpegResult = Assert.Single(report.Results, r => r.ComponentName == "FFmpeg / FFprobe");
        Assert.False(ffmpegResult.IsAvailable);
        Assert.NotNull(ffmpegResult.RemediationGuidance);

        // The other two checks are independent and must still run and pass.
        Assert.True(report.Results.Single(r => r.ComponentName == "Visual C++ Runtime").IsAvailable);
        Assert.True(report.Results.Single(r => r.ComponentName == "OpenCV native library").IsAvailable);
    }

    [Fact]
    public async Task RunAsync_FfmpegIncompatible_ReportsFfmpegFailure()
    {
        var service = new NativeDependencyDiagnosticsService(
            new FakeFfmpegToolLocator(exceptionToThrow: new FfmpegToolsIncompatibleException("ffmpeg.exe", "boom")),
            FakeOpenCvNativeProbe.Succeeding(),
            new FakeNativeLibraryProbe());

        var report = await service.RunAsync(CancellationToken.None);

        Assert.False(report.AllPassed);
        Assert.False(report.Results.Single(r => r.ComponentName == "FFmpeg / FFprobe").IsAvailable);
    }

    [Fact]
    public async Task RunAsync_VcRuntimeLibraryMissing_ReportsWhichLibraries()
    {
        var service = new NativeDependencyDiagnosticsService(
            new FakeFfmpegToolLocator(),
            FakeOpenCvNativeProbe.Succeeding(),
            new FakeNativeLibraryProbe("vcruntime140.dll", "msvcp140.dll"));

        var report = await service.RunAsync(CancellationToken.None);

        Assert.False(report.AllPassed);
        var vcResult = report.Results.Single(r => r.ComponentName == "Visual C++ Runtime");
        Assert.False(vcResult.IsAvailable);
        Assert.Contains("vcruntime140.dll", vcResult.Detail);
        Assert.Contains("msvcp140.dll", vcResult.Detail);
        Assert.DoesNotContain("vcruntime140_1.dll", vcResult.Detail);
        Assert.NotNull(vcResult.RemediationGuidance);
    }

    [Fact]
    public async Task RunAsync_OpenCvProbeThrows_ReportsOpenCvFailureWithoutPropagating()
    {
        var service = new NativeDependencyDiagnosticsService(
            new FakeFfmpegToolLocator(),
            FakeOpenCvNativeProbe.Throwing(new DllNotFoundException("Unable to load DLL 'OpenCvSharpExtern'")),
            new FakeNativeLibraryProbe());

        var report = await service.RunAsync(CancellationToken.None);

        Assert.False(report.AllPassed);
        var openCvResult = report.Results.Single(r => r.ComponentName == "OpenCV native library");
        Assert.False(openCvResult.IsAvailable);
        Assert.Contains("OpenCvSharpExtern", openCvResult.Detail);
        Assert.NotNull(openCvResult.RemediationGuidance);
    }

    [Fact]
    public async Task RunAsync_OpenCvProbeSucceeds_DetailUsesFirstLineOfBuildInformation()
    {
        var service = new NativeDependencyDiagnosticsService(
            new FakeFfmpegToolLocator(),
            FakeOpenCvNativeProbe.Succeeding("General configuration for OpenCV 4.13.0\n  Version control: 4.13.0"),
            new FakeNativeLibraryProbe());

        var report = await service.RunAsync(CancellationToken.None);

        var openCvResult = report.Results.Single(r => r.ComponentName == "OpenCV native library");
        Assert.Equal("General configuration for OpenCV 4.13.0", openCvResult.Detail);
    }
}
