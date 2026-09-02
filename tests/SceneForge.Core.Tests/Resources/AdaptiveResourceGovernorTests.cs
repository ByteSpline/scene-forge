using SceneForge.Core.Resources;

namespace SceneForge.Core.Tests.Resources;

public class AdaptiveResourceGovernorTests
{
    // Product requirement (tightened from the original "leave one core
    // free" Phase 13 design after a real hang was traced to ffmpeg/OpenCV
    // saturating every core): SceneForge must never use more than ~35% of
    // this machine's total CPU capacity at once, on any machine, at any
    // pipeline stage. MaxWorkers is the one number every ffmpeg -threads
    // argument and every OpenCV thread-pool size in the app derives from -
    // see IAdaptiveResourceGovernor's remarks.
    [Fact]
    public void MaxWorkers_EightLogicalCpus_CapsWellUnderThirtyFivePercent()
    {
        var governor = new AdaptiveResourceGovernor(new FakeDriveInfoProvider(availableFreeBytes: long.MaxValue), processorCount: 8);

        // floor(8 * 0.35) = floor(2.8) = 2 workers (25% of 8 cores) - flooring
        // (never rounding up) is what guarantees the cap is never exceeded.
        Assert.Equal(2, governor.MaxWorkers);
    }

    [Fact]
    public void MaxWorkers_SingleLogicalCpu_NeverGoesBelowOne()
    {
        // A one-core machine cannot honor a sub-100% budget and still make
        // progress; like the previous "leave one free" design, 1 is the
        // deliberate floor even though it necessarily exceeds 35% here.
        var governor = new AdaptiveResourceGovernor(new FakeDriveInfoProvider(availableFreeBytes: long.MaxValue), processorCount: 1);

        Assert.Equal(1, governor.MaxWorkers);
    }

    [Fact]
    public void MaxWorkers_TwoLogicalCpus_NeverGoesBelowOne()
    {
        // floor(2 * 0.35) = 0, floored back up to the 1-worker minimum
        // (50% of 2 cores) - the same small-machine exception as above.
        var governor = new AdaptiveResourceGovernor(new FakeDriveInfoProvider(availableFreeBytes: long.MaxValue), processorCount: 2);

        Assert.Equal(1, governor.MaxWorkers);
    }

    // The hard invariant the product owner asked to be verified by test:
    // for any machine with enough logical CPUs that a sub-35% budget is
    // even representable as a whole worker (>= 3 logical CPUs, since
    // floor(3 * 0.35) = 1), MaxWorkers/processorCount must never exceed
    // 0.35. Below 3 logical CPUs the 1-worker minimum unavoidably exceeds
    // the ratio (covered by the two single/dual-core tests above) - that is
    // a documented, necessary exception, not a violation of this invariant.
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void MaxWorkers_NeverExceedsThirtyFivePercentCpuBudget(int processorCount)
    {
        var governor = new AdaptiveResourceGovernor(new FakeDriveInfoProvider(availableFreeBytes: long.MaxValue), processorCount);

        var ratio = governor.MaxWorkers / (double)processorCount;

        Assert.True(governor.MaxWorkers >= 1, "MaxWorkers must never be zero - it would stall the pipeline entirely.");
        Assert.True(
            ratio <= 0.35,
            $"MaxWorkers={governor.MaxWorkers} of {processorCount} logical CPUs is {ratio:P1}, over the 35% hard CPU budget.");
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
