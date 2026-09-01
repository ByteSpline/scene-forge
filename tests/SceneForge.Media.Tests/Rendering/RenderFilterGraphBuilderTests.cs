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

    // Regression coverage for a real, measured bug distinct from (and not
    // fixed by) RenderPlanBuilder's per-segment duration quantization:
    // ffmpeg's fps filter (converting a segment's source frame rate to
    // spec.FrameRate) duplicates/drops frames based on presentation
    // timestamps, not on the trim window's exact requested duration, and
    // can emit more frames than spec.FrameRate.ToFrameCount(segment.SourceDuration)
    // calls for whenever the source's native rate differs from spec.FrameRate
    // (verified directly against real ffmpeg 9.0.1: a 30fps source trimmed
    // to a frame-exact-at-25fps duration and converted with fps=25/1 alone
    // produced a consistent +1 frame per segment, and 60 such segments
    // concatenated measured 488 actual frames against 470 expected - see
    // RenderFilterGraphBuilder.BuildSegmentFilter's own remarks). A second,
    // FRAME-domain trim (start_frame/end_frame, which counts actual output
    // frames rather than reading timestamps) right after fps= forces every
    // segment back to exactly its intended frame count regardless of the
    // fps filter's own boundary behavior - this only asserts the graph
    // carries that second trim in the right place; the real-ffmpeg,
    // frame-rate-mismatched, at-scale proof lives in
    // FFmpegRenderServiceIntegrationTests' three
    // RenderAsync_RealFfmpegSourceFrameRateDiffersFromOutputFrameRate_*_VerifiesWithinTolerance
    // tests (SinglePass/Batched/DistinctDedup).
    [Fact]
    public void Build_SegmentDuration_EmitsFrameDomainTrimRightAfterFpsPinningExactFrameCount()
    {
        var plan = CreatePlan([CreateSegment(0, 0, 0.28)]); // 0.28s @ 25fps = exactly 7 frames

        var graph = RenderFilterGraphBuilder.Build(plan);

        Assert.Contains("fps=25/1,trim=start_frame=0:end_frame=7,setpts=PTS-STARTPTS,format=yuv420p", graph);
    }

    [Fact]
    public void Build_SeekedVideoConcat_AlsoEmitsFrameDomainTrimPinningExactFrameCount()
    {
        var segments = new[] { CreateSegment(0, 0, 0.28) };
        var spec = new RenderOutputSpec { Width = 640, Height = 360, FrameRate = TwentyFiveFps, FitMode = AspectFitMode.Letterbox };

        var graph = RenderFilterGraphBuilder.BuildSeekedVideoConcat(segments, spec, rotationDegrees: 0);

        Assert.Contains("fps=25/1,trim=start_frame=0:end_frame=7,setpts=PTS-STARTPTS,format=yuv420p", graph);
    }
}
