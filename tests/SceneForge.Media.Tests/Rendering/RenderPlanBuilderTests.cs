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
    public void Build_ValidRequest_PlannedVideoDurationIsSumOfFrameQuantizedSegmentDurations()
    {
        // 3.0s is already frame-exact at 25fps (75 frames); 2.5s is exactly
        // the midpoint between 62 and 63 frames (62.5), so
        // MidpointRounding.AwayFromZero rounds it up to 63 frames = 2.52s -
        // see the dedicated quantization tests below for the (more common)
        // non-midpoint case. This supersedes the old assumption that
        // PlannedVideoDuration equals the raw TimelinePlan.PlannedDuration
        // (5.5s) verbatim - that equality was exactly the bug root-caused
        // below (see docs/OPTIMIZATION_REPORT.md). Built from the same
        // RationalFrameRate.FromFrameCount the production code itself uses
        // (rather than TimeSpan.FromSeconds(5.52), a double-arithmetic
        // literal that lands one tick off) so this compares tick-for-tick
        // against the exact value RenderPlanBuilder computes.
        var placements = new[]
        {
            TimelinePlanBuilder.CreatePlacement(0, 0, 0, 3),
            TimelinePlanBuilder.CreatePlacement(1, 1, 5, 2.5),
        };
        var request = CreateRequest(placements);

        var plan = _builder.Build(request);

        var expected = TwentyFiveFps.FromFrameCount(75) + TwentyFiveFps.FromFrameCount(63);
        Assert.Equal(expected, plan.PlannedVideoDuration);
    }

    [Fact]
    public void Build_PlacementDurationNotFrameAligned_QuantizesSegmentDurationToNearestOutputFrame()
    {
        // Root cause (see docs/OPTIMIZATION_REPORT.md's investigation,
        // verified directly against real ffmpeg 9.0.1): ffmpeg's trim
        // filter keeps every source frame whose presentation time falls
        // within [start, start+duration) - for a duration that is not an
        // exact multiple of the output frame period, the last frame that
        // STARTS inside the window is kept in full even though the
        // window's nominal end falls partway through that frame's own
        // display period, so passing a non-frame-aligned duration straight
        // through produces however many frames happen to overlap the
        // window, not a value SceneForge chose. Quantizing here - to the
        // NEAREST whole frame via RationalFrameRate.ToFrameCount's own
        // MidpointRounding.AwayFromZero, the same convention TimelinePlanner
        // already uses for its own target-duration quantization - means
        // the exact duration handed to ffmpeg's trim filter is always
        // already an exact multiple of the frame period, which (verified
        // directly against real ffmpeg) always then produces exactly that
        // many frames with zero further rounding ambiguity. 1/3 second at
        // 25fps is 8.333 frames, which is nearer to 8 than 9.
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, sourceStartSeconds: 0, sourceDurationSeconds: 1.0 / 3.0) };
        var request = CreateRequest(placements);

        var plan = _builder.Build(request);

        var expected = TwentyFiveFps.FromFrameCount(8);
        Assert.Equal(expected, plan.Segments[0].SourceDuration);
        Assert.Equal(expected, plan.PlannedVideoDuration);
    }

    [Fact]
    public void Build_MultipleNonFrameAlignedPlacements_PlannedVideoDurationIsSumOfQuantizedSegments()
    {
        // The actual regression this phase found: each segment quantizes
        // independently (ffmpeg's trim filter has no knowledge of any
        // other segment), so PlannedVideoDuration must be the sum of the
        // ACTUAL quantized segment durations, not the raw
        // TimelinePlan.PlannedDuration - otherwise the accumulated
        // per-segment rounding grows with clip count (2 segments of 1/3s
        // each previously produced a real 0.72s render verified against a
        // 0.6667s "expected," failing by more than the verifier's 1-frame
        // tolerance - reproduced via FFmpegRenderServiceIntegrationTests
        // and directly against real ffmpeg outside SceneForge entirely).
        var placements = new[]
        {
            TimelinePlanBuilder.CreatePlacement(0, 0, sourceStartSeconds: 0, sourceDurationSeconds: 1.0 / 3.0),
            TimelinePlanBuilder.CreatePlacement(1, 1, sourceStartSeconds: 1.0 / 3.0, sourceDurationSeconds: 1.0 / 3.0),
        };
        var request = CreateRequest(placements);

        var plan = _builder.Build(request);

        var expectedPerSegment = TwentyFiveFps.FromFrameCount(8);
        Assert.Equal(expectedPerSegment, plan.Segments[0].SourceDuration);
        Assert.Equal(expectedPerSegment, plan.Segments[1].SourceDuration);
        Assert.Equal(TwentyFiveFps.FromFrameCount(16), plan.PlannedVideoDuration);
    }

    [Fact]
    public void Build_PlacementQuantizesToZeroFrames_ThrowsRenderPlanException()
    {
        // 0.01s at 25fps is 0.25 frames, which rounds to 0 - a degenerate
        // zero-duration trim ffmpeg cannot encode. Must fail loudly here
        // rather than silently building a RenderPlan that would fail (or
        // worse, silently drop a segment) once handed to ffmpeg.
        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, sourceStartSeconds: 0, sourceDurationSeconds: 0.01) };
        var request = CreateRequest(placements);

        Assert.Throws<RenderPlanException>(() => _builder.Build(request));
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
