using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;
using SceneForge.Media.Rendering;
using SceneForge.Media.Rendering.Internal;
using SceneForge.Media.Tests.TestSupport;
using Xunit.Abstractions;

namespace SceneForge.Media.Tests.Rendering;

// Empirical confirmation of the motivating real-world bug (found in manual
// end-to-end testing after Phase 16): with very little clean footage (19
// clips, ~67s total) against a 22-minute audio target, TimelinePlanner's
// never-short-output guarantee (Phase 16) is mathematically correct but
// produces a placement list several hundred entries long. The pre-existing
// single-filter_complex-graph render architecture (Phase 9) then emits one
// trim->scale->pad->fps->format->setsar chain PLUS one implicit split
// output PER PLACEMENT, so the ffmpeg filtergraph grows with the *total*
// segment count rather than the *distinct clip* count - at this ratio that
// is a ~2,700-node graph fed by a ~380-way split off a single decoded
// input, which real ffmpeg 9.x fails to allocate ("Cannot allocate
// memory") or schedules so slowly the render is effectively infeasible
// (~17h estimated). These tests pin the numbers that make the two-stage
// pre-render-and-concat strategy (see FFmpegRenderService) necessary; they
// are the "before" evidence for that change (CLAUDE.md rule 9).
public sealed class HighRepetitionRenderScenarioTests
{
    private static readonly RationalFrameRate ThirtyFps = new(30, 1);

    private readonly ITestOutputHelper _output;

    public HighRepetitionRenderScenarioTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // 19 clips drawn from a ~24-minute source, 3.4-3.6s each, ~66.4s total -
    // the "very limited clean footage" end of the real report.
    private static List<CleanClip> BuildLimitedFootagePool()
    {
        var clips = new List<CleanClip>(19);
        for (var i = 0; i < 19; i++)
        {
            var duration = i % 2 == 0 ? 3.4 : 3.6;
            clips.Add(CleanClipBuilder.Create(
                startSeconds: i * 70.0,
                durationSeconds: duration,
                sourceSceneIndex: i,
                clusterId: i % 6));
        }

        return clips;
    }

    private static TimelinePlan PlanTwentyTwoMinuteTarget(IReadOnlyList<CleanClip> clips) =>
        new TimelinePlanner().Plan(new TimelinePlanRequest
        {
            AvailableClips = clips,
            TargetAudioDuration = TimeSpan.FromMinutes(22),
            OutputTimeBase = ThirtyFps,
            Seed = 1,
        });

    [Fact]
    public void LimitedFootageAgainstLongTarget_PlannerProducesSeveralHundredPlacements()
    {
        var clips = BuildLimitedFootagePool();
        var totalFootage = clips.Aggregate(TimeSpan.Zero, (sum, c) => sum + c.Range.Duration);

        var plan = PlanTwentyTwoMinuteTarget(clips);

        _output.WriteLine($"Clean footage: {totalFootage.TotalSeconds:F1}s across {clips.Count} clips");
        _output.WriteLine($"Target: {TimeSpan.FromMinutes(22).TotalSeconds:F0}s ({TimeSpan.FromMinutes(22) / totalFootage:F1}x the pool)");
        _output.WriteLine($"Placements produced: {plan.Placements.Count}");
        _output.WriteLine($"Max reuse of any one clip: {plan.Placements.Max(p => p.UsageOrdinal)}");

        Assert.True(plan.IsComplete);
        Assert.Equal(plan.QuantizedTargetDuration, plan.PlannedDuration);

        // The exact count depends on the seeded shuffle and per-lap trimming,
        // but it is always "several hundred" for this ratio - far more than
        // any single filter_complex graph can carry. This range is
        // deliberately wide: its job is to document the order of magnitude,
        // not to freeze one arithmetic result.
        Assert.InRange(plan.Placements.Count, 340, 430);
    }

    [Fact]
    public void LimitedFootageAgainstLongTarget_SingleFilterComplexGraphIsInfeasiblyLarge()
    {
        var clips = BuildLimitedFootagePool();
        var plan = PlanTwentyTwoMinuteTarget(clips);

        // Map the plan to a RenderPlan directly (RenderPlanBuilder would
        // additionally require the source file to exist on disk) - the same
        // segment-per-placement translation it performs, which is exactly
        // the mapping under test here.
        var segments = plan.Placements
            .Select(p => new RenderSegment
            {
                Position = p.Position,
                SourceStart = p.SourceRange.Start,
                SourceDuration = p.UsedDuration,
                IsTrimmed = p.IsTrimmed,
            })
            .ToList();
        var renderPlan = new RenderPlan
        {
            SourceFilePath = "source.mp4",
            Segments = segments,
            OutputSpec = new RenderOutputSpec { Width = 1920, Height = 1080, FrameRate = ThirtyFps, FitMode = AspectFitMode.Letterbox },
            Audio = new RenderAudioTrackSpec { FilePath = "audio.m4a", TrimStart = TimeSpan.Zero, TrimDuration = plan.PlannedDuration },
            SourceRotationDegrees = 0,
            PlannedVideoDuration = plan.PlannedDuration,
        };

        Assert.Equal(plan.Placements.Count, renderPlan.Segments.Count);

        var distinctSegments = renderPlan.Segments
            .Select(s => (s.SourceStart, s.SourceDuration))
            .Distinct()
            .Count();

        var filterGraph = RenderFilterGraphBuilder.Build(renderPlan);
        // trim, setpts, scale, pad, fps, format, setsar per segment, plus the
        // implicit split output and the concat input - a conservative lower
        // bound on the libavfilter node count ffmpeg must allocate at once.
        var approxFilterNodes = renderPlan.Segments.Count * 7;

        _output.WriteLine($"Segments: {renderPlan.Segments.Count}, distinct source ranges: {distinctSegments}");
        _output.WriteLine($"filter_complex graph length: {filterGraph.Length:N0} chars");
        _output.WriteLine($"approx libavfilter nodes: {approxFilterNodes:N0} + a {renderPlan.Segments.Count}-way split + a {renderPlan.Segments.Count}-way concat");

        // Distinct source ranges never exceeds the pool size (+1 for the
        // trimmed final placement) - this is the whole point: the render only
        // needs ~20 real encodes, not ~380.
        Assert.True(distinctSegments <= clips.Count + 1);

        // The graph is tens of thousands of characters and thousands of
        // nodes - well past InlineFilterGraphCharacterThreshold and well into
        // the range where ffmpeg 9.x fails to allocate the filtergraph.
        Assert.True(filterGraph.Length > 40_000, $"expected a very large graph, got {filterGraph.Length} chars");
        Assert.True(approxFilterNodes > 2_000);
    }

    // A DIFFERENT failing shape from the 19-clip case above: a source with
    // plenty of clean footage produces a large pool, so the 22-minute
    // target is reached with little or NO repetition - the plan is hundreds
    // of DISTINCT segments. The single-pass filter_complex graph would be
    // the same ~2,300-node graph ffmpeg 9.x fails to allocate, just built
    // from distinct trims instead of repeated ones - so this shape must be
    // routed to the Batched pre-render strategy (the distinct-dedup path,
    // keyed on repetition, does not and should not catch it).
    [Fact]
    public void PlentifulFootageLowRepetition_RoutesToBatchedStrategy_NotTheInfeasibleSinglePassGraph()
    {
        // 420 clips, 3.0-5.0s (cycling in 0.5s steps like CleanClipExtractor's
        // own 3-5s default range), ~1680s of clean footage - comfortably
        // more than the 1320s target, so no reuse relaxation is needed.
        var clips = new List<CleanClip>(420);
        for (var i = 0; i < 420; i++)
        {
            clips.Add(CleanClipBuilder.Create(
                startSeconds: i * 6.0,
                durationSeconds: 3.0 + (i % 5) * 0.5,
                sourceSceneIndex: i % 60,
                clusterId: i % 25));
        }

        var plan = PlanTwentyTwoMinuteTarget(clips);

        var segments = plan.Placements
            .Select(p => new RenderSegment
            {
                Position = p.Position,
                SourceStart = p.SourceRange.Start,
                SourceDuration = p.UsedDuration,
                IsTrimmed = p.IsTrimmed,
            })
            .ToList();
        var renderPlan = new RenderPlan
        {
            SourceFilePath = "source.mp4",
            Segments = segments,
            OutputSpec = new RenderOutputSpec { Width = 1920, Height = 1080, FrameRate = ThirtyFps, FitMode = AspectFitMode.Letterbox },
            Audio = new RenderAudioTrackSpec { FilePath = "audio.m4a", TrimStart = TimeSpan.Zero, TrimDuration = plan.PlannedDuration },
            SourceRotationDegrees = 0,
            PlannedVideoDuration = plan.PlannedDuration,
        };

        var distinctSegments = renderPlan.Segments.Select(s => (s.SourceStart, s.SourceDuration)).Distinct().Count();
        var maxReuse = plan.Placements.Max(p => p.UsageOrdinal);
        var filterGraph = RenderFilterGraphBuilder.Build(renderPlan);

        _output.WriteLine($"Placements: {renderPlan.Segments.Count}, distinct: {distinctSegments}, max reuse of any clip: {maxReuse}");
        _output.WriteLine($"filter_complex graph length: {filterGraph.Length:N0} chars, approx nodes: {renderPlan.Segments.Count * 7:N0}");
        _output.WriteLine($"SelectRenderStrategy: {FFmpegRenderService.SelectRenderStrategy(renderPlan)}");

        Assert.True(plan.IsComplete);
        Assert.Equal(plan.QuantizedTargetDuration, plan.PlannedDuration);

        // Little or no repetition - essentially every placement is a distinct trim.
        Assert.True(maxReuse <= 2, $"expected ~no repetition, but max reuse was {maxReuse}");
        Assert.True(distinctSegments > renderPlan.Segments.Count * 0.5, "this scenario must be dominated by distinct segments");

        // Several hundred segments; the single-pass graph would be infeasible...
        Assert.InRange(renderPlan.Segments.Count, 280, 380);
        Assert.True(filterGraph.Length > 40_000);

        // ...so the strategy selector routes it to Batched (not SinglePass,
        // and not DistinctDedup - there is nothing to dedup here).
        Assert.Equal(FFmpegRenderService.RenderStrategy.Batched, FFmpegRenderService.SelectRenderStrategy(renderPlan));
    }
}
