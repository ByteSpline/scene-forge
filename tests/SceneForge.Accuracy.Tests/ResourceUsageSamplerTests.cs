using SceneForge.Accuracy.Evaluation;

namespace SceneForge.Accuracy.Tests;

public class ResourceUsageSamplerTests
{
    [Fact]
    public async Task Sampler_AlwaysReportsAtLeastOneSample_EvenForAnInstantSpan()
    {
        await using var sampler = new ResourceUsageSampler();

        Assert.True(sampler.PeakManagedMemoryBytes > 0);
        Assert.True(sampler.PeakWorkingSetBytes > 0);
    }

    [Fact]
    public async Task Sampler_CapturesAPeakReachedDuringTheSpan_EvenAfterItIsReleased()
    {
        await using var sampler = new ResourceUsageSampler();

        // Allocate, hold across at least one sample tick, then release -
        // the peak must still reflect it even though it is gone by the
        // time DisposeAsync's final sample runs.
        var block = new byte[64 * 1024 * 1024];
        Array.Fill(block, (byte)1);
        await Task.Delay(200);
        var peakDuringHold = sampler.PeakManagedMemoryBytes;
        block = [];
        GC.KeepAlive(block);

        Assert.True(peakDuringHold >= 64 * 1024 * 1024, $"Expected the sampler to have observed the 64MB allocation, but peak was {peakDuringHold:N0} bytes.");
        Assert.True(sampler.PeakManagedMemoryBytes >= peakDuringHold, "Peak must never decrease once observed.");
    }
}
