using SceneForge.Core.Resources;

namespace SceneForge.Core.Tests.Resources;

public class AdaptiveResourceGovernorTests
{
    [Fact]
    public void MaxWorkers_LeavesOneLogicalCpuFree()
    {
        var governor = new AdaptiveResourceGovernor(new FakeDriveInfoProvider(availableFreeBytes: long.MaxValue), processorCount: 8);

        Assert.Equal(7, governor.MaxWorkers);
    }

    [Fact]
    public void MaxWorkers_SingleLogicalCpu_NeverGoesBelowOne()
    {
        var governor = new AdaptiveResourceGovernor(new FakeDriveInfoProvider(availableFreeBytes: long.MaxValue), processorCount: 1);

        Assert.Equal(1, governor.MaxWorkers);
    }

    [Fact]
    public void MaxWorkers_TwoLogicalCpus_ReturnsOne()
    {
        var governor = new AdaptiveResourceGovernor(new FakeDriveInfoProvider(availableFreeBytes: long.MaxValue), processorCount: 2);

        Assert.Equal(1, governor.MaxWorkers);
    }

    [Fact]
    public void EnsureSufficientDiskSpace_AvailableExceedsRequired_DoesNotThrow()
    {
        var governor = new AdaptiveResourceGovernor(new FakeDriveInfoProvider(availableFreeBytes: 1_000_000), processorCount: 4);

        var exception = Record.Exception(() => governor.EnsureSufficientDiskSpace(@"C:\some\path", requiredBytes: 500_000));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureSufficientDiskSpace_AvailableEqualsRequired_DoesNotThrow()
    {
        var governor = new AdaptiveResourceGovernor(new FakeDriveInfoProvider(availableFreeBytes: 500_000), processorCount: 4);

        var exception = Record.Exception(() => governor.EnsureSufficientDiskSpace(@"C:\some\path", requiredBytes: 500_000));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureSufficientDiskSpace_AvailableBelowRequired_ThrowsInsufficientDiskSpaceExceptionNamingBoth()
    {
        var governor = new AdaptiveResourceGovernor(new FakeDriveInfoProvider(availableFreeBytes: 100), processorCount: 4);

        var exception = Assert.Throws<InsufficientDiskSpaceException>(
            () => governor.EnsureSufficientDiskSpace(@"C:\some\path", requiredBytes: 500_000));

        Assert.Equal(@"C:\some\path", exception.Path);
        Assert.Equal(500_000, exception.RequiredBytes);
        Assert.Equal(100, exception.AvailableBytes);
    }

    [Fact]
    public void EnsureSufficientDiskSpace_IsAnIOException_SoExistingRecognizedFailureHandlingCatchesIt()
    {
        // AnalysisProgressViewModel/RenderProgressViewModel already treat a
        // bare IOException as a recognized, user-facing failure rather than
        // an unhandled crash - deriving from IOException means a low-disk
        // failure gets that same treatment for free, with no new catch
        // clause needed at either call site.
        Assert.IsAssignableFrom<IOException>(new InsufficientDiskSpaceException(@"C:\x", 1, 0));
    }

    private sealed class FakeDriveInfoProvider(long availableFreeBytes) : IDriveInfoProvider
    {
        public long GetAvailableFreeBytes(string path) => availableFreeBytes;
    }
}
