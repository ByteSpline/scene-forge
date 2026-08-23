using SceneForge.Media.Domain;
using SceneForge.Media.Planning;
using SceneForge.Media.Rendering;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Validation;

namespace SceneForge.Media.Tests.Rendering;

public class RenderPlanBuilderTests
{
    private static readonly RationalFrameRate TwentyFiveFps = new(25, 1);

    private readonly RenderPlanBuilder _builder = new();

    private static readonly string SourceVideoPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_video_audio.mp4");
    private static readonly string AudioPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_audio_only.m4a");

    private static RenderOutputSpec CreateOutputSpec(AspectFitMode fitMode = AspectFitMode.Letterbox) => new()
    {
        Width = 640,
        Height = 360,
        FrameRate = TwentyFiveFps,
        FitMode = fitMode,
    };

    private static RenderAudioTrackSpec CreateAudioSpec(double durationSeconds = 6.0) => new()
    {
        FilePath = AudioPath,
        TrimStart = TimeSpan.Zero,
        TrimDuration = TimeSpan.FromSeconds(durationSeconds),
    };

    private static RenderPlanRequest CreateRequest(
        IReadOnlyList<TimelinePlacement> placements,
        MediaInfo? sourceMediaInfo = null,
        RenderOutputSpec? outputSpec = null,
        RenderAudioTrackSpec? audio = null) => new()
        {
            TimelinePlan = TimelinePlanBuilder.CreatePlan(placements, TwentyFiveFps),
            SourceFilePath = SourceVideoPath,
            SourceMediaInfo = sourceMediaInfo ?? MediaInfoBuilder.CreateVideoWithAudio(SourceVideoPath, durationSeconds: 120),
            OutputSpec = outputSpec ?? CreateOutputSpec(),
            Audio = audio ?? CreateAudioSpec(),
        };

    [Fact]
    public void Build_NullRequest_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _builder.Build(null!));
    }

    [Fact]
    public void Build_EmptyPlacements_Throws()
    {
        var request = CreateRequest([]);

        Assert.Throws<RenderPlanException>(() => _builder.Build(request));
    }

    [Fact]
    public void Build_NoVideoStreamInSourceMediaInfo_Throws()
    {
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, 0, 3) };
        var request = CreateRequest(placements, sourceMediaInfo: MediaInfoBuilder.CreateVideoWithAudio(SourceVideoPath) with { VideoStreams = [] });

        Assert.Throws<RenderPlanException>(() => _builder.Build(request));
    }

    [Fact]
    public void Build_MissingSourceFile_ThrowsMediaValidationException()
    {
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, 0, 3) };
        var request = CreateRequest(placements) with { SourceFilePath = Path.Combine(AppContext.BaseDirectory, "does-not-exist.mp4") };

        Assert.Throws<MediaValidationException>(() => _builder.Build(request));
    }

    [Fact]
    public void Build_MissingAudioFile_ThrowsMediaValidationException()
    {
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, 0, 3) };
        var request = CreateRequest(placements, audio: CreateAudioSpec() with { FilePath = Path.Combine(AppContext.BaseDirectory, "does-not-exist.m4a") });

        Assert.Throws<MediaValidationException>(() => _builder.Build(request));
    }

    [Fact]
    public void Build_UndefinedOutputFrameRate_Throws()
    {
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, 0, 3) };
        var request = CreateRequest(placements, outputSpec: CreateOutputSpec() with { FrameRate = RationalFrameRate.Undefined });

        Assert.Throws<RenderPlanException>(() => _builder.Build(request));
    }

    [Fact]
    public void Build_SegmentExceedsProbedSourceDuration_Throws()
    {
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, sourceStartSeconds: 100, sourceDurationSeconds: 10) };
        var request = CreateRequest(placements, sourceMediaInfo: MediaInfoBuilder.CreateVideoWithAudio(SourceVideoPath, durationSeconds: 105));

        Assert.Throws<RenderPlanException>(() => _builder.Build(request));
    }

    [Fact]
    public void Build_SegmentWithinSmallProbeSlack_Succeeds()
    {
        // 100 + 10 = 110s requested, source reports 109.8s - within the
        // 500ms container-rounding slack RenderPlanBuilder allows.
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, sourceStartSeconds: 100, sourceDurationSeconds: 10) };
        var request = CreateRequest(placements, sourceMediaInfo: MediaInfoBuilder.CreateVideoWithAudio(SourceVideoPath, durationSeconds: 109.8));

        var plan = _builder.Build(request);

        Assert.Single(plan.Segments);
    }

    [Fact]
    public void Build_NegativeAudioTrimStart_Throws()
    {
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, 0, 3) };
        var request = CreateRequest(placements, audio: CreateAudioSpec() with { TrimStart = TimeSpan.FromSeconds(-1) });

        Assert.Throws<RenderPlanException>(() => _builder.Build(request));
    }

    [Fact]
    public void Build_ZeroAudioTrimDuration_Throws()
    {
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, 0, 3) };
        var request = CreateRequest(placements, audio: CreateAudioSpec() with { TrimDuration = TimeSpan.Zero });

        Assert.Throws<RenderPlanException>(() => _builder.Build(request));
    }

    [Fact]
    public void Build_ValidRequest_ProducesSegmentsInPositionOrder()
    {
        var placements = new[]
        {
            TimelinePlanBuilder.CreatePlacement(1, 5, sourceStartSeconds: 10, sourceDurationSeconds: 3),
            TimelinePlanBuilder.CreatePlacement(0, 2, sourceStartSeconds: 0, sourceDurationSeconds: 4),
        };
        var request = CreateRequest(placements);

        var plan = _builder.Build(request);

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(0, plan.Segments[0].Position);
        Assert.Equal(TimeSpan.FromSeconds(0), plan.Segments[0].SourceStart);
        Assert.Equal(TimeSpan.FromSeconds(4), plan.Segments[0].SourceDuration);
        Assert.Equal(1, plan.Segments[1].Position);
        Assert.Equal(TimeSpan.FromSeconds(10), plan.Segments[1].SourceStart);
        Assert.Equal(TimeSpan.FromSeconds(3), plan.Segments[1].SourceDuration);
    }

    [Fact]
    public void Build_ValidRequest_CarriesSourceRotationDegreesFromMediaInfo()
    {
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, 0, 3) };
        var request = CreateRequest(placements, sourceMediaInfo: MediaInfoBuilder.CreateVideoWithAudio(SourceVideoPath, durationSeconds: 60, rotationDegrees: 90));

        var plan = _builder.Build(request);

        Assert.Equal(90, plan.SourceRotationDegrees);
    }

    [Fact]
    public void Build_ValidRequest_PlannedVideoDurationMatchesTimelinePlanPlannedDuration()
    {
        var placements = new[]
        {
            TimelinePlanBuilder.CreatePlacement(0, 0, 0, 3),
            TimelinePlanBuilder.CreatePlacement(1, 1, 5, 2.5),
        };
        var request = CreateRequest(placements);

        var plan = _builder.Build(request);

        Assert.Equal(TimeSpan.FromSeconds(5.5), plan.PlannedVideoDuration);
    }

    [Fact]
    public void Build_ValidRequest_ResolvesAudioFilePathToFullPath()
    {
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, 0, 3) };
        var request = CreateRequest(placements);

        var plan = _builder.Build(request);

        Assert.Equal(Path.GetFullPath(AudioPath), plan.Audio.FilePath);
    }

    [Fact]
    public void Build_TrimmedFinalPlacement_PreservesIsTrimmedFlag()
    {
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, sourceStartSeconds: 0, sourceDurationSeconds: 5, usedDurationSeconds: 2, isTrimmed: true) };
        var request = CreateRequest(placements);

        var plan = _builder.Build(request);

        Assert.True(plan.Segments[0].IsTrimmed);
        Assert.Equal(TimeSpan.FromSeconds(2), plan.Segments[0].SourceDuration);
    }
}
