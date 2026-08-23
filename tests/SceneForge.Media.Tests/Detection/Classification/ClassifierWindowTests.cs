using SceneForge.Media.Detection.Classification;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection.Classification;

public class ClassifierWindowTests
{
    [Fact]
    public void Append_WithinMaxSpan_KeepsAllSamples()
    {
        var window = new ClassifierWindow(TimeSpan.FromSeconds(2));

        window.Append(FrameSignalSampleBuilder.Sample(0));
        window.Append(FrameSignalSampleBuilder.Sample(1));
        window.Append(FrameSignalSampleBuilder.Sample(2));

        Assert.Equal(3, window.Samples.Count);
    }

    [Fact]
    public void Append_ExceedingMaxSpan_EvictsOldestSamples()
    {
        // Each Sample spans 0.25s; a 0.5s max span should only ever retain
        // enough trailing samples to cover that span.
        var window = new ClassifierWindow(TimeSpan.FromSeconds(0.5));

        for (var i = 0; i < 20; i++)
        {
            window.Append(FrameSignalSampleBuilder.Sample(i));
        }

        Assert.True(window.Samples.Count <= 3);
        Assert.Equal(TimeSpan.FromSeconds(19 * 0.25), window.Samples[^1].PreviousTimestamp);
        Assert.Equal(TimeSpan.FromSeconds(20 * 0.25), window.Samples[^1].Timestamp);
    }
}
