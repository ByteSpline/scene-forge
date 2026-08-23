using SceneForge.Media.Domain;
using SceneForge.Media.Rendering;
using SceneForge.Media.Rendering.Internal;

namespace SceneForge.Media.Tests.Rendering;

public class RenderFilterGraphBuilderTests
{
    private static readonly RationalFrameRate TwentyFiveFps = new(25, 1);

    private static RenderPlan CreatePlan(
        IReadOnlyList<RenderSegment> segments,
        AspectFitMode fitMode = AspectFitMode.Letterbox,
        int rotationDegrees = 0)
    {
        var plannedDuration = segments.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.SourceDuration);
        return new RenderPlan
        {
            SourceFilePath = "source.mp4",
            Segments = segments,
            OutputSpec = new RenderOutputSpec { Width = 640, Height = 360, FrameRate = TwentyFiveFps, FitMode = fitMode },
            Audio = new RenderAudioTrackSpec { FilePath = "audio.m4a", TrimDuration = plannedDuration },
            SourceRotationDegrees = rotationDegrees,
            PlannedVideoDuration = plannedDuration,
        };
    }

    private static RenderSegment CreateSegment(int position, double startSeconds, double durationSeconds, bool isTrimmed = false) => new()
    {
        Position = position,
        SourceStart = TimeSpan.FromSeconds(startSeconds),
        SourceDuration = TimeSpan.FromSeconds(durationSeconds),
        IsTrimmed = isTrimmed,
    };

    [Fact]
    public void Build_OneSegment_ReferencesSourceInputAndProducesVideoAndAudioOutputLabels()
    {
        var plan = CreatePlan([CreateSegment(0, 0, 3)]);

        var graph = RenderFilterGraphBuilder.Build(plan);

        Assert.Contains("[0:v]trim=start=0:duration=3", graph);
        Assert.Contains("[1:a]atrim=start=0:duration=3", graph);
        Assert.Contains(RenderFilterGraphBuilder.VideoOutputLabel, graph);
        Assert.Contains(RenderFilterGraphBuilder.AudioOutputLabel, graph);
    }

    [Fact]
    public void Build_NeverReferencesSourceAudioStream()
    {
        var plan = CreatePlan([CreateSegment(0, 0, 3), CreateSegment(1, 10, 2)]);

        var graph = RenderFilterGraphBuilder.Build(plan);

        Assert.DoesNotContain("[0:a]", graph);
    }

    [Fact]
    public void Build_MultipleSegments_ConcatenatesInPositionOrder()
    {
        var plan = CreatePlan([CreateSegment(0, 0, 3), CreateSegment(1, 10, 2), CreateSegment(2, 20, 1)]);

        var graph = RenderFilterGraphBuilder.Build(plan);

        Assert.Contains("[v0][v1][v2]concat=n=3:v=1:a=0" + RenderFilterGraphBuilder.VideoOutputLabel, graph);
    }

    [Theory]
    [InlineData(AspectFitMode.Letterbox, "pad=640:360")]
    [InlineData(AspectFitMode.Fill, "crop=640:360")]
    [InlineData(AspectFitMode.Stretch, "scale=640:360:flags=bicubic")]
    public void Build_AppliesFitModeSpecificFilter(AspectFitMode fitMode, string expectedFragment)
    {
        var plan = CreatePlan([CreateSegment(0, 0, 3)], fitMode: fitMode);

        var graph = RenderFilterGraphBuilder.Build(plan);

        Assert.Contains(expectedFragment, graph);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(90, "transpose=1")]
    [InlineData(180, "hflip,vflip")]
    [InlineData(270, "transpose=2")]
    public void Build_AppliesRotationFilterMatchingSourceRotationDegrees(int rotationDegrees, string expectedFragment)
    {
        var plan = CreatePlan([CreateSegment(0, 0, 3)], rotationDegrees: rotationDegrees);

        var graph = RenderFilterGraphBuilder.Build(plan);

        if (expectedFragment.Length == 0)
        {
            Assert.DoesNotContain("transpose", graph);
            Assert.DoesNotContain("hflip", graph);
        }
        else
        {
            Assert.Contains(expectedFragment, graph);
        }
    }

    [Fact]
    public void Build_EverySegmentEndsWithSharedFpsFormatAndSar()
    {
        var plan = CreatePlan([CreateSegment(0, 0, 3), CreateSegment(1, 10, 2)]);

        var graph = RenderFilterGraphBuilder.Build(plan);
        var segmentFilters = graph.Split(';').Where(part => part.StartsWith("[0:v]", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, segmentFilters.Count);
        Assert.All(segmentFilters, filter =>
        {
            Assert.Contains("fps=25/1", filter);
            Assert.Contains("format=yuv420p", filter);
            Assert.Contains("setsar=1/1", filter);
        });
    }
}
