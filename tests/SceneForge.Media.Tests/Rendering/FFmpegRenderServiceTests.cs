using SceneForge.Core.Resources;
using SceneForge.Media.Domain;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Rendering;
using SceneForge.Media.Rendering.Internal;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Tooling;
using SceneForge.Media.Validation;

namespace SceneForge.Media.Tests.Rendering;

// Exercises FFmpegRenderService's argument-building, single-vs-fallback
// encoder handling, filter-script threshold, and verification wiring
// entirely against fakes (FakeProcessRunner/FakeFfmpegToolLocator/
// FakeFfprobeService and a fake IHardwareEncoderProbe) - no real ffmpeg
// process is spawned. See FFmpegRenderServiceIntegrationTests for the real-
// binary end-to-end coverage.
public sealed class FFmpegRenderServiceTests : IDisposable
{
    private static readonly RationalFrameRate TwentyFiveFps = new(25, 1);

    private readonly string _outputDirectory;

    public FFmpegRenderServiceTests()
    {
        _outputDirectory = Path.Combine(Path.GetTempPath(), "SceneForgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    private static RenderSegment CreateSegment(int position, double startSeconds, double durationSeconds) => new()
    {
        Position = position,
        SourceStart = TimeSpan.FromSeconds(startSeconds),
        SourceDuration = TimeSpan.FromSeconds(durationSeconds),
        IsTrimmed = false,
    };

    private static RenderPlan CreatePlan(int segmentCount = 1)
    {
        var segments = Enumerable.Range(0, segmentCount)
            .Select(i => CreateSegment(i, i * 10.0, 3.0))
            .ToList();
        var plannedDuration = segments.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.SourceDuration);

        return new RenderPlan
        {
            SourceFilePath = "source.mp4",
            Segments = segments,
            OutputSpec = new RenderOutputSpec { Width = 640, Height = 360, FrameRate = TwentyFiveFps },
            Audio = new RenderAudioTrackSpec { FilePath = "audio.m4a", TrimDuration = plannedDuration },
            SourceRotationDegrees = 0,
            PlannedVideoDuration = plannedDuration,
        };
    }

    private static VideoEncoderSelection HardwareSelection => new()
    {
        Kind = VideoEncoderKind.NvidiaNvenc,
        FfmpegEncoderName = "h264_nvenc",
        IsHardwareAccelerated = true,
    };

    private static VideoEncoderSelection SoftwareSelection => new()
    {
        Kind = VideoEncoderKind.SoftwareX264,
        FfmpegEncoderName = "libx264",
        IsHardwareAccelerated = false,
    };

    private static IAdaptiveResourceGovernor AlwaysSufficientResourceGovernor => new FakeAdaptiveResourceGovernor();

    private sealed class FakeAdaptiveResourceGovernor : IAdaptiveResourceGovernor
    {
        public int MaxWorkers => 4;

        public void EnsureSufficientDiskSpace(string path, long requiredBytes)
        {
        }
    }

    private sealed class FakeEncoderProbe : IHardwareEncoderProbe
    {
        private readonly VideoEncoderSelection _selection;
        private readonly VideoEncoderSelection _softwareSelection;

        public FakeEncoderProbe(VideoEncoderSelection selection, VideoEncoderSelection? softwareSelection = null)
        {
            _selection = selection;
            _softwareSelection = softwareSelection ?? SoftwareSelection;
        }

        public Task<VideoEncoderSelection> SelectEncoderAsync(CancellationToken cancellationToken) => Task.FromResult(_selection);

        public Task<VideoEncoderSelection> SelectSoftwareEncoderAsync(CancellationToken cancellationToken) => Task.FromResult(_softwareSelection);
    }

    private static bool IsRenderInvocation(ProcessExecutionRequest request) => request.Arguments.Contains("-progress");

    private static bool IsConcatStageAInvocation(ProcessExecutionRequest request) =>
        request.Arguments.Contains("-an") && request.Arguments.Contains("-frames:v") && !request.Arguments.Contains("concat");

    private static bool IsConcatStageBInvocation(ProcessExecutionRequest request)
    {
        var args = request.Arguments.ToList();
        var fIndex = args.IndexOf("-f");
        return fIndex >= 0 && fIndex + 1 < args.Count && args[fIndex + 1] == "concat";
    }

    // The filter graph an invocation carries, whether inline (-filter_complex)
    // or written to a script file (-/filter_complex) - the file still exists
    // while the fake process "runs".
    private static string ReadFilterGraph(ProcessExecutionRequest request)
    {
        var args = request.Arguments.ToList();
        var inline = args.IndexOf("-filter_complex");
        if (inline >= 0)
        {
            return args[inline + 1];
        }

        var fromFile = args.IndexOf("-/filter_complex");
        return fromFile >= 0 ? File.ReadAllText(args[fromFile + 1]) : string.Empty;
    }

    private static int CountTrims(string filterGraph) =>
        System.Text.RegularExpressions.Regex.Matches(filterGraph, "trim=start=").Count;

    // segmentCount placements drawn from distinctWindowCount distinct source
    // windows (round-robin), so a caller can dial in exactly the
    // high-repetition shape ShouldUseConcatDemuxerStrategy keys on.
    private static RenderPlan CreateRepeatingPlan(int segmentCount, int distinctWindowCount)
    {
        var windows = Enumerable.Range(0, distinctWindowCount)
            .Select(w => (Start: w * 5.0, Duration: 2.0))
            .ToArray();

        var segments = Enumerable.Range(0, segmentCount)
            .Select(i =>
            {
                var window = windows[i % distinctWindowCount];
                return new RenderSegment
                {
                    Position = i,
                    SourceStart = TimeSpan.FromSeconds(window.Start),
                    SourceDuration = TimeSpan.FromSeconds(window.Duration),
                    IsTrimmed = false,
                };
            })
            .ToList();
        var plannedDuration = segments.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.SourceDuration);

        return new RenderPlan
        {
            SourceFilePath = "source.mp4",
            Segments = segments,
            OutputSpec = new RenderOutputSpec { Width = 640, Height = 360, FrameRate = TwentyFiveFps },
            Audio = new RenderAudioTrackSpec { FilePath = "audio.m4a", TrimDuration = plannedDuration },
            SourceRotationDegrees = 0,
            PlannedVideoDuration = plannedDuration,
        };
    }

    private static RenderOutputVerifier CreatePassingVerifier(FakeProcessRunner processRunner, TimeSpan expectedDuration)
    {
        var mediaInfo = MediaInfoBuilder.CreateVideoWithAudio("out.mp4", durationSeconds: expectedDuration.TotalSeconds);
        var ffprobeService = FakeFfprobeService.ReturningMediaInfo(mediaInfo);
        return new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());
    }

    private static RenderOutputVerifier CreateFailingVerifier(FakeProcessRunner processRunner)
    {
        // Reports a video-only file with double the expected duration -
        // fails both the audio-stream-count and duration-tolerance checks.
        var mediaInfo = MediaInfoBuilder.CreateVideoOnly("out.mp4", durationSeconds: 999);
        var ffprobeService = FakeFfprobeService.ReturningMediaInfo(mediaInfo);
        return new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());
    }

    // Reports a DURATION-ONLY problem (audio stream present, both endpoint
    // frames decodable) whose duration is drawn from durationsPerCall in
    // order - one entry consumed per RenderOutputVerifier.VerifyAsync call,
    // the last entry repeating for any call beyond the array's length. Lets
    // a test simulate "the first N verify calls miss tolerance, then a
    // later one lands exactly on plan.PlannedVideoDuration" without needing
    // a bespoke fake per scenario.
    private static RenderOutputVerifier CreateSequencedDurationOnlyVerifier(FakeProcessRunner processRunner, params TimeSpan[] durationsPerCall)
    {
        var call = 0;
        var ffprobeService = new FakeFfprobeService((_, _) =>
        {
            var duration = durationsPerCall[Math.Min(call, durationsPerCall.Length - 1)];
            call++;
            return Task.FromResult(MediaInfoBuilder.CreateVideoWithAudio("out.mp4", durationSeconds: duration.TotalSeconds));
        });
        return new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());
    }

    private static bool IsDurationCorrectionInvocation(ProcessExecutionRequest request) =>
        request.Arguments.Contains("-vf") && request.Arguments.Any(a => a.Contains("tpad=stop_mode=clone", StringComparison.Ordinal));

    [Fact]
    public async Task RenderAsync_NullPlan_Throws()
    {
        var processRunner = FakeProcessRunner.ReturningResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, TimeSpan.FromSeconds(3)), AlwaysSufficientResourceGovernor);

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RenderAsync(null!, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None));
    }

    [Fact]
    public async Task RenderAsync_OutputPathEqualsSourceFile_ThrowsMediaValidationException()
    {
        var plan = CreatePlan() with { SourceFilePath = Path.Combine(_outputDirectory, "same.mp4") };
        var processRunner = FakeProcessRunner.ReturningResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await Assert.ThrowsAsync<MediaValidationException>(() => service.RenderAsync(plan, Path.Combine(_outputDirectory, "same.mp4"), null, CancellationToken.None));
    }

    [Fact]
    public async Task RenderAsync_HardwareEncoderSucceeds_ReturnsResultWithoutFallback()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((request, _) => Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero }));
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(HardwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        var result = await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.False(result.FellBackToSoftwareEncoder);
        Assert.Equal(VideoEncoderKind.NvidiaNvenc, result.Encoder.Kind);
        Assert.True(result.Verification.IsValid);

        var renderCalls = processRunner.Requests.Count(IsRenderInvocation);
        Assert.Equal(1, renderCalls);
    }

    [Fact]
    public async Task RenderAsync_HardwareEncoderFails_FallsBackToLibx264AndSucceeds()
    {
        var plan = CreatePlan();
        var renderAttempt = 0;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                renderAttempt++;
                var exitCode = renderAttempt == 1 ? 1 : 0;
                return Task.FromResult(new ProcessExecutionResult { ExitCode = exitCode, StandardOutput = "", StandardError = "simulated failure", Elapsed = TimeSpan.Zero });
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(HardwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        var result = await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.True(result.FellBackToSoftwareEncoder);
        Assert.Equal(VideoEncoderKind.SoftwareX264, result.Encoder.Kind);
        Assert.Equal(2, renderAttempt);
    }

    [Fact]
    public async Task RenderAsync_HardwareAndSoftwareBothFail_ThrowsRenderExecutionException()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            var exitCode = IsRenderInvocation(request) ? 1 : 0;
            return Task.FromResult(new ProcessExecutionResult { ExitCode = exitCode, StandardOutput = "", StandardError = "boom", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(HardwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await Assert.ThrowsAsync<RenderExecutionException>(() => service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None));
    }

    [Fact]
    public async Task RenderAsync_SoftwareEncoderFails_ThrowsImmediatelyWithoutRetry()
    {
        var plan = CreatePlan();
        var renderAttempts = 0;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                renderAttempts++;
            }

            var exitCode = IsRenderInvocation(request) ? 1 : 0;
            return Task.FromResult(new ProcessExecutionResult { ExitCode = exitCode, StandardOutput = "", StandardError = "boom", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await Assert.ThrowsAsync<RenderExecutionException>(() => service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None));
        Assert.Equal(1, renderAttempts);
    }

    [Fact]
    public async Task RenderAsync_VerificationFails_ThrowsRenderVerificationExceptionCarryingResult()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero }));
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreateFailingVerifier(processRunner), AlwaysSufficientResourceGovernor);

        var exception = await Assert.ThrowsAsync<RenderVerificationException>(() => service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None));

        Assert.False(exception.Result.IsValid);
        Assert.NotEmpty(exception.Result.Failures);
    }

    [Fact]
    public async Task RenderAsync_ReportsProgressParsedFromFfmpegStdout()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                request.OutputProgress?.Report(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "frame=5"));
                request.OutputProgress?.Report(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "out_time_us=1000000"));
                request.OutputProgress?.Report(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "speed=1x"));
                request.OutputProgress?.Report(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "progress=end"));
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        var progress = new RecordingProgress<RenderProgress>();

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), progress, CancellationToken.None);

        Assert.Single(progress.Reports);
        Assert.Equal(5, progress.Reports[0].FrameNumber);
        Assert.True(progress.Reports[0].IsFinished);
    }

    [Fact]
    public async Task RenderAsync_SinglePassGraphPastCharThreshold_UsesFilterComplexScriptFile_AndDeletesItAfterward()
    {
        // A plan right at the single-pass ceiling (InitialBatchSegmentCount)
        // whose graph still exceeds the inline command-line threshold, so
        // the single-pass path takes its write-to-file branch.
        var plan = CreatePlan(segmentCount: FFmpegRenderService.InitialBatchSegmentCount);
        Assert.Equal(FFmpegRenderService.RenderStrategy.SinglePass, FFmpegRenderService.SelectRenderStrategy(plan));
        Assert.True(
            RenderFilterGraphBuilder.Build(plan).Length > FFmpegRenderService.InlineFilterGraphCharacterThreshold,
            "this test must exercise the filter-script file branch");

        string? observedScriptPath = null;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                var args = request.Arguments.ToList();

                // The removed '-filter_complex_script' option must never be
                // emitted - a current ffmpeg (>= 8.0) rejects the entire
                // invocation with "Unrecognized option
                // 'filter_complex_script'". The large graph is handed over
                // with ffmpeg's generic read-from-file form instead.
                Assert.DoesNotContain("-filter_complex_script", args);

                var scriptIndex = args.IndexOf("-/filter_complex");
                Assert.True(scriptIndex >= 0, "Expected -/filter_complex <file> for a large single-pass graph.");
                observedScriptPath = request.Arguments[scriptIndex + 1];
                Assert.True(File.Exists(observedScriptPath), "Filter script must exist while ffmpeg is running.");
                Assert.DoesNotContain("-filter_complex", args);
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.NotNull(observedScriptPath);
        Assert.False(File.Exists(observedScriptPath), "Filter script must be deleted after the render process exits.");
    }

    [Fact]
    public async Task RenderAsync_FewSegments_UsesInlineFilterComplex()
    {
        var plan = CreatePlan(segmentCount: 2);
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                Assert.Contains("-filter_complex", request.Arguments);
                Assert.DoesNotContain("-filter_complex_script", request.Arguments);
                Assert.DoesNotContain("-/filter_complex", request.Arguments);
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_NeverMapsSourceAudioStream()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                Assert.DoesNotContain("0:a", request.Arguments);
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_HighRepetitionPlan_PreRendersEachDistinctSegmentOnceThenConcats()
    {
        // 300 placements, 10 distinct windows - well past
        // ConcatDemuxerSegmentThreshold and far below the distinct/total
        // ratio, so the concat-demuxer strategy is taken.
        var plan = CreateRepeatingPlan(segmentCount: 300, distinctWindowCount: 10);
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero }));
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        // Exactly one Stage A encode per distinct window, exactly one Stage B
        // concat pass - never one ffmpeg node per placement.
        Assert.Equal(10, processRunner.Requests.Count(IsConcatStageAInvocation));
        Assert.Equal(1, processRunner.Requests.Count(IsConcatStageBInvocation));

        var stageA = processRunner.Requests.First(IsConcatStageAInvocation).Arguments.ToList();
        Assert.Contains("-filter_complex", stageA);
        Assert.Contains("-frames:v", stageA);
        Assert.Contains("-an", stageA);
        Assert.DoesNotContain("-progress", stageA);

        var stageB = processRunner.Requests.First(IsConcatStageBInvocation).Arguments.ToList();
        Assert.Equal("concat", stageB[stageB.IndexOf("-f") + 1]);
        Assert.Contains("-safe", stageB);
        Assert.Equal("copy", stageB[stageB.IndexOf("-c:v") + 1]);
        Assert.DoesNotContain("0:a", stageB);
    }

    [Fact]
    public async Task RenderAsync_HighRepetitionPlan_ConcatListHasOneLinePerPlacementInTimelineOrder()
    {
        var plan = CreateRepeatingPlan(segmentCount: 250, distinctWindowCount: 8);
        string? listContent = null;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsConcatStageBInvocation(request))
            {
                var args = request.Arguments.ToList();
                var listPath = args[args.IndexOf("-i") + 1];
                listContent = File.ReadAllText(listPath);
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.NotNull(listContent);
        var lines = listContent!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(250, lines.Length);
        Assert.All(lines, line => Assert.StartsWith("file '", line));

        // Placement i uses distinct window (i % 8); the same window always
        // resolves to the same pre-rendered file, so the list's file cycle
        // has period 8.
        Assert.Equal(lines[0], lines[8]);
        Assert.NotEqual(lines[0], lines[1]);
    }

    [Fact]
    public async Task RenderAsync_HighRepetitionPlan_DeletesPreRenderWorkingDirectoryAfterward()
    {
        var plan = CreateRepeatingPlan(segmentCount: 200, distinctWindowCount: 6);
        string? workingDirectory = null;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsConcatStageBInvocation(request))
            {
                var args = request.Arguments.ToList();
                workingDirectory = Path.GetDirectoryName(args[args.IndexOf("-i") + 1]);
                Assert.True(Directory.Exists(workingDirectory), "Working directory must exist while ffmpeg is running.");
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.NotNull(workingDirectory);
        Assert.False(Directory.Exists(workingDirectory), "Pre-render working directory must be deleted after the render completes.");
    }

    [Fact]
    public async Task RenderAsync_LargeAllDistinctPlan_UsesBatchedPreRender()
    {
        // 305 placements, all distinct - past the single-pass ceiling with
        // NO repetition, so DistinctDedup does not apply. The Batched
        // strategy renders the timeline in bounded filter_complex batches
        // and concat-demuxes the batch outputs.
        var plan = CreateRepeatingPlan(segmentCount: 305, distinctWindowCount: 305);
        Assert.Equal(FFmpegRenderService.RenderStrategy.Batched, FFmpegRenderService.SelectRenderStrategy(plan));

        string? listContent = null;
        var batchTrimCounts = new List<int>();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsConcatStageAInvocation(request))
            {
                batchTrimCounts.Add(CountTrims(ReadFilterGraph(request)));
            }
            else if (IsConcatStageBInvocation(request))
            {
                var args = request.Arguments.ToList();
                listContent = File.ReadAllText(args[args.IndexOf("-i") + 1]);
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        // ceil(305 / 60) = 6 batches, each a bounded filter_complex encode; one concat pass.
        var expectedBatches = (int)Math.Ceiling(305.0 / FFmpegRenderService.InitialBatchSegmentCount);
        Assert.Equal(expectedBatches, processRunner.Requests.Count(IsConcatStageAInvocation));
        Assert.Equal(1, processRunner.Requests.Count(IsConcatStageBInvocation));
        // The only progress-reporting ffmpeg invocation is the concat pass -
        // the single-pass render path (also progress-reporting) never ran.
        Assert.Equal(1, processRunner.Requests.Count(IsRenderInvocation));
        Assert.All(processRunner.Requests.Where(IsRenderInvocation), r => Assert.True(IsConcatStageBInvocation(r)));

        // No per-batch filter graph carries more than InitialBatchSegmentCount trims,
        // and every segment is accounted for exactly once across the batches.
        Assert.All(batchTrimCounts, count => Assert.InRange(count, 1, FFmpegRenderService.InitialBatchSegmentCount));
        Assert.Equal(305, batchTrimCounts.Sum());

        Assert.NotNull(listContent);
        Assert.Equal(expectedBatches, listContent!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task RenderAsync_HighRepetitionPlan_HardwareStageAFails_FallsBackToLibx264ForAllSegments()
    {
        var plan = CreateRepeatingPlan(segmentCount: 180, distinctWindowCount: 5);
        var attempts = new List<string>();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsConcatStageAInvocation(request))
            {
                var encoderName = request.Arguments.ToList()[request.Arguments.ToList().IndexOf("-c:v") + 1];
                attempts.Add(encoderName);
                var exitCode = encoderName == "h264_nvenc" ? 1 : 0;
                return Task.FromResult(new ProcessExecutionResult { ExitCode = exitCode, StandardOutput = "", StandardError = "nvenc boom", Elapsed = TimeSpan.Zero });
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(HardwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        var result = await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.True(result.FellBackToSoftwareEncoder);
        Assert.Equal(VideoEncoderKind.SoftwareX264, result.Encoder.Kind);
        // First attempt bails after the first distinct segment fails; the
        // libx264 retry re-encodes all 5 distinct segments.
        Assert.Equal("h264_nvenc", attempts[0]);
        Assert.Equal(5, attempts.Count(a => a == "libx264"));
        // Stage B (stream copy) never re-encodes video, so it is never a
        // fallback trigger.
        Assert.Equal(1, processRunner.Requests.Count(IsConcatStageBInvocation));
    }

    [Fact]
    public async Task RenderAsync_BatchedPlan_HardwareBatchFails_FallsBackToLibx264ForAllBatches()
    {
        var plan = CreateRepeatingPlan(segmentCount: 305, distinctWindowCount: 305); // -> Batched, 6 batches
        var attempts = new List<string>();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsConcatStageAInvocation(request))
            {
                var encoderName = request.Arguments.ToList()[request.Arguments.ToList().IndexOf("-c:v") + 1];
                attempts.Add(encoderName);
                var exitCode = encoderName == "h264_nvenc" ? 1 : 0;
                return Task.FromResult(new ProcessExecutionResult { ExitCode = exitCode, StandardOutput = "", StandardError = "nvenc boom", Elapsed = TimeSpan.Zero });
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(HardwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        var result = await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.True(result.FellBackToSoftwareEncoder);
        Assert.Equal(VideoEncoderKind.SoftwareX264, result.Encoder.Kind);
        Assert.Equal("h264_nvenc", attempts[0]);
        // libx264 retry re-renders every batch; ceil(305/60) = 6.
        Assert.Equal(6, attempts.Count(a => a == "libx264"));
        Assert.Equal(1, processRunner.Requests.Count(IsConcatStageBInvocation));
    }

    [Fact]
    public async Task RenderAsync_HighRepetitionPlan_ReportsProgressAcrossBothStages()
    {
        var plan = CreateRepeatingPlan(segmentCount: 200, distinctWindowCount: 4);
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsConcatStageBInvocation(request))
            {
                request.OutputProgress?.Report(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "out_time_us=200000000"));
                request.OutputProgress?.Report(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "progress=end"));
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);
        var progress = new RecordingProgress<RenderProgress>();

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), progress, CancellationToken.None);

        // 4 Stage A updates (one per distinct segment) + at least one Stage B update.
        Assert.True(progress.Reports.Count >= 5, $"expected >=5 progress reports, got {progress.Reports.Count}");
        Assert.True(progress.Reports.Select(r => r.OutTime).SequenceEqual(progress.Reports.Select(r => r.OutTime).OrderBy(t => t)), "progress OutTime must be monotonically non-decreasing");
        Assert.True(progress.Reports[^1].IsFinished);
    }

    // --- Input-seeking (performance fix 1): each batch/dedup ffmpeg must
    // seek directly to every segment's SourceStart with an input-level
    // '-ss <n> -i <source>' pair, instead of one shared '-i' that decodes
    // the whole source from frame 0 for every batch. -------------------------

    // The '-ss <value>' input-seek pairs in front of the filter graph, in
    // order. Each entry is (seekSeconds, isFollowedByInputOfSource).
    private static List<(double SeekSeconds, bool ThenInput)> InputSeeks(ProcessExecutionRequest request, string sourcePath)
    {
        var args = request.Arguments.ToList();
        var graphAt = args.FindIndex(a => a is "-filter_complex" or "-/filter_complex");
        var scan = graphAt < 0 ? args.Count : graphAt;
        var seeks = new List<(double, bool)>();
        for (var i = 0; i < scan; i++)
        {
            if (args[i] != "-ss")
            {
                continue;
            }

            var seconds = double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            var thenInput = i + 3 < args.Count && args[i + 2] == "-i" && args[i + 3] == sourcePath;
            seeks.Add((seconds, thenInput));
        }

        return seeks;
    }

    [Fact]
    public async Task RenderAsync_BatchedPlan_SeeksToEverySegmentSourceStart_NotASingleFullDecode()
    {
        // 305 distinct segments -> Batched, 6 bounded batches. Segment i's
        // SourceStart is i*5s (CreateRepeatingPlan), so a Position-ordered
        // batch spans a wide, scattered source range - exactly the shape a
        // single shared decode reads almost the whole source for.
        var plan = CreateRepeatingPlan(segmentCount: 305, distinctWindowCount: 305);
        Assert.Equal(FFmpegRenderService.RenderStrategy.Batched, FFmpegRenderService.SelectRenderStrategy(plan));

        // Read the graph and seeks INSIDE the handler - the working directory
        // (and any filter-script file) is deleted once the render returns.
        var batches = new List<(List<(double SeekSeconds, bool ThenInput)> Seeks, string Graph)>();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsConcatStageAInvocation(request))
            {
                batches.Add((InputSeeks(request, plan.SourceFilePath), ReadFilterGraph(request)));
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.Equal(6, batches.Count);

        var expectedStartsInOrder = plan.Segments
            .OrderBy(s => s.Position)
            .Select(s => Math.Round(s.SourceStart.TotalSeconds, 6))
            .ToList();
        var seenStarts = new List<double>();
        foreach (var (seeks, graph) in batches)
        {
            // Every segment in the batch is reached by its own '-ss <start> -i <source>'.
            Assert.NotEmpty(seeks);
            Assert.All(seeks, s => Assert.True(s.ThenInput, "each -ss must be immediately followed by '-i <source>'"));

            // The graph reads one input per segment ([0:v],[1:v],...), each
            // trimming from 0 because the input is already seeked - never a
            // single [0:v] trimming from a large absolute timestamp.
            Assert.Equal(seeks.Count, CountTrims(graph));
            // Every trim starts at 0 (the -ss already positioned the input) -
            // never a large absolute source timestamp.
            Assert.Equal(seeks.Count, System.Text.RegularExpressions.Regex.Matches(graph, @"trim=start=0:").Count);
            for (var k = 0; k < seeks.Count; k++)
            {
                Assert.Contains($"[{k}:v]trim=start=0:", graph);
            }

            seenStarts.AddRange(seeks.Select(s => Math.Round(s.SeekSeconds, 6)));
        }

        // Across all batches, exactly the plan's segment SourceStarts, once each, in Position order.
        Assert.Equal(expectedStartsInOrder, seenStarts);
    }

    [Fact]
    public async Task RenderAsync_DistinctDedupPlan_SeeksToEachDistinctWindowStart()
    {
        var plan = CreateRepeatingPlan(segmentCount: 300, distinctWindowCount: 10);
        Assert.Equal(FFmpegRenderService.RenderStrategy.DistinctDedup, FFmpegRenderService.SelectRenderStrategy(plan));

        var runs = new List<(List<(double SeekSeconds, bool ThenInput)> Seeks, string Graph)>();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsConcatStageAInvocation(request))
            {
                runs.Add((InputSeeks(request, plan.SourceFilePath), ReadFilterGraph(request)));
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.Equal(10, runs.Count);
        var distinctStarts = plan.Segments.Select(s => Math.Round(s.SourceStart.TotalSeconds, 6)).Distinct().OrderBy(v => v).ToList();
        var seekedStarts = new List<double>();
        foreach (var (seeks, graph) in runs)
        {
            var single = Assert.Single(seeks);
            Assert.True(single.ThenInput);
            seekedStarts.Add(Math.Round(single.SeekSeconds, 6));
            Assert.Contains("[0:v]trim=start=0:", graph);
        }

        Assert.Equal(distinctStarts, seekedStarts.OrderBy(v => v).ToList());
    }

    [Fact]
    public async Task RenderAsync_BatchedPlan_StillPinsConcatenatedFrameCount_AfterSeekChange()
    {
        // Phase 16 frame-exact guarantee: -frames:v on each Stage A batch
        // still equals the sum of that batch's per-segment quantized frame
        // counts, unchanged by the switch to per-segment input seeks.
        var plan = CreateRepeatingPlan(segmentCount: 130, distinctWindowCount: 130); // -> Batched, 3 batches (60/60/10)
        var stageAFrameCounts = new List<long>();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsConcatStageAInvocation(request))
            {
                var args = request.Arguments.ToList();
                stageAFrameCounts.Add(long.Parse(args[args.IndexOf("-frames:v") + 1], System.Globalization.CultureInfo.InvariantCulture));
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        var expectedTotal = plan.Segments.Sum(s => Math.Max(1, plan.OutputSpec.FrameRate.ToFrameCount(s.SourceDuration)));
        Assert.Equal(expectedTotal, stageAFrameCounts.Sum());
    }

    // --- Encoder selection logging (performance fix 2) --------------------

    private sealed class CapturingTraceListener : System.Diagnostics.TraceListener
    {
        private readonly System.Text.StringBuilder _buffer = new();

        public string Text
        {
            get
            {
                lock (_buffer)
                {
                    return _buffer.ToString();
                }
            }
        }

        public override void Write(string? message)
        {
            lock (_buffer)
            {
                _buffer.Append(message);
            }
        }

        public override void WriteLine(string? message)
        {
            lock (_buffer)
            {
                _buffer.Append(message).Append('\n');
            }
        }
    }

    [Theory]
    [InlineData(true, "h264_nvenc", "hardware-accelerated")]
    [InlineData(false, "libx264", "software")]
    public async Task RenderAsync_LogsWhichEncoderTheProbeSelected(bool hardware, string encoderName, string accelWord)
    {
        var plan = CreatePlan();
        var selection = new VideoEncoderSelection { Kind = hardware ? VideoEncoderKind.NvidiaNvenc : VideoEncoderKind.SoftwareX264, FfmpegEncoderName = encoderName, IsHardwareAccelerated = hardware };
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero }));
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(selection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        var listener = new CapturingTraceListener();
        System.Diagnostics.Trace.Listeners.Add(listener);
        try
        {
            await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);
        }
        finally
        {
            System.Diagnostics.Trace.Listeners.Remove(listener);
        }

        Assert.Contains($"encoder probe selected '{encoderName}'", listener.Text);
        Assert.Contains(accelWord, listener.Text);
    }

    [Fact]
    public async Task RenderAsync_HardwareRenderFails_LogsTheSoftwareEncoderItRetriesWith()
    {
        var plan = CreatePlan();
        var attempt = 0;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                attempt++;
                return Task.FromResult(new ProcessExecutionResult { ExitCode = attempt == 1 ? 1 : 0, StandardOutput = "", StandardError = "gpu boom", Elapsed = TimeSpan.Zero });
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var software = new VideoEncoderSelection { Kind = VideoEncoderKind.SoftwareOpenH264, FfmpegEncoderName = "libopenh264", IsHardwareAccelerated = false };
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(HardwareSelection, software), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration), AlwaysSufficientResourceGovernor);

        var listener = new CapturingTraceListener();
        System.Diagnostics.Trace.Listeners.Add(listener);
        RenderResult result;
        try
        {
            result = await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);
        }
        finally
        {
            System.Diagnostics.Trace.Listeners.Remove(listener);
        }

        // The fallback resolves to whatever the probe reports as the software
        // encoder - never a hardcoded name - and that choice is logged.
        Assert.True(result.FellBackToSoftwareEncoder);
        Assert.Equal("libopenh264", result.Encoder.FfmpegEncoderName);
        Assert.Contains("retrying the whole render with software encoder 'libopenh264'", listener.Text);
    }

    // --- Duration-only verification self-correction (never a red "Render
    // failed" for a duration-tolerance miss - see
    // docs/RENDER_DURATION_SELF_CORRECTION.md) -----------------------------

    [Theory]
    [InlineData(true, true, true, true, true, false)] // everything else ok, duration within tolerance -> not a failure at all
    [InlineData(false, true, true, true, true, true)] // duration-only miss
    [InlineData(false, false, true, true, true, false)] // duration AND missing video stream -> not duration-only
    [InlineData(false, true, false, true, true, false)] // duration AND wrong audio-stream count -> not duration-only
    [InlineData(false, true, true, false, true, false)] // duration AND first frame undecodable -> not duration-only
    [InlineData(false, true, true, true, false, false)] // duration AND last frame undecodable -> not duration-only
    [InlineData(true, false, true, true, true, false)] // missing video stream alone, duration fine -> not duration-only (DurationWithinTolerance is true)
    public void IsDurationOnlyFailure_ClassifiesExactlyDurationAloneMisses(
        bool durationWithinTolerance, bool hasVideo, bool hasOneAudio, bool firstOk, bool lastOk, bool expected)
    {
        var result = new RenderVerificationResult
        {
            HasExpectedVideoStream = hasVideo,
            HasExactlyOneAudioStream = hasOneAudio,
            ExpectedDuration = TimeSpan.FromSeconds(3),
            ActualDuration = TimeSpan.FromSeconds(durationWithinTolerance ? 3 : 2),
            DurationDelta = TimeSpan.FromSeconds(durationWithinTolerance ? 0 : 1),
            DurationTolerance = TimeSpan.FromMilliseconds(40),
            DurationWithinTolerance = durationWithinTolerance,
            FirstFrameDecodable = firstOk,
            MiddleFrameDecodable = true,
            LastFrameDecodable = lastOk,
        };

        Assert.Equal(expected, FFmpegRenderService.IsDurationOnlyFailure(result));
    }

    [Fact]
    public async Task RenderAsync_DurationOnlyMismatch_SameEncoderRetryRecovers_SucceedsWithoutSurfacingError()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero }));
        var verifier = CreateSequencedDurationOnlyVerifier(processRunner, plan.PlannedVideoDuration - TimeSpan.FromSeconds(1), plan.PlannedVideoDuration);
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), verifier, AlwaysSufficientResourceGovernor);

        var result = await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.True(result.Verification.IsValid);
        Assert.Equal(2, processRunner.Requests.Count(IsRenderInvocation));
        Assert.Equal(RenderDurationCorrectionKind.SameEncoderRetry, Assert.Single(result.DurationCorrections).Kind);
    }

    [Fact]
    public async Task RenderAsync_DurationOnlyMismatchPersistsThroughSameEncoderRetry_ForcesSoftwareEncoderRetry_SucceedsWithoutSurfacingError()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero }));
        var badDuration = plan.PlannedVideoDuration - TimeSpan.FromSeconds(1);
        var verifier = CreateSequencedDurationOnlyVerifier(processRunner, badDuration, badDuration, plan.PlannedVideoDuration);
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(HardwareSelection), verifier, AlwaysSufficientResourceGovernor);

        var result = await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.True(result.Verification.IsValid);
        Assert.True(result.FellBackToSoftwareEncoder);
        Assert.Equal(VideoEncoderKind.SoftwareX264, result.Encoder.Kind);
        Assert.Equal(3, processRunner.Requests.Count(IsRenderInvocation));
        Assert.Equal(
            [RenderDurationCorrectionKind.SameEncoderRetry, RenderDurationCorrectionKind.ForcedSoftwareEncoderRetry],
            result.DurationCorrections.Select(c => c.Kind));
    }

    [Fact]
    public async Task RenderAsync_DurationOnlyMismatchPersistsThroughBothRetries_AppliesFrameExactRemux_SucceedsWithoutSurfacingError()
    {
        var plan = CreatePlan();
        var outputPath = Path.Combine(_outputDirectory, "out.mp4");
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsDurationCorrectionInvocation(request))
            {
                // RenderDurationCorrector re-processes and swaps in a real
                // file (File.Delete + File.Move) after a "successful" ffmpeg
                // pass - the fake process never actually writes ffmpeg
                // output, so the corrected-copy path must exist for real.
                File.WriteAllBytes(request.Arguments[^1], []);
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var badDuration = plan.PlannedVideoDuration - TimeSpan.FromSeconds(1);
        var verifier = CreateSequencedDurationOnlyVerifier(processRunner, badDuration, badDuration, badDuration, plan.PlannedVideoDuration);
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), verifier, AlwaysSufficientResourceGovernor);

        var result = await service.RenderAsync(plan, outputPath, null, CancellationToken.None);

        Assert.True(result.Verification.IsValid);
        Assert.Equal(
            [RenderDurationCorrectionKind.SameEncoderRetry, RenderDurationCorrectionKind.ForcedSoftwareEncoderRetry, RenderDurationCorrectionKind.FrameExactRemux],
            result.DurationCorrections.Select(c => c.Kind));
        Assert.Single(processRunner.Requests, IsDurationCorrectionInvocation);
    }

    [Fact]
    public async Task RenderAsync_DurationOnlyMismatchNeverRecovers_ThrowsRenderVerificationException_OnlyAfterExhaustingAllThreeTiers()
    {
        var plan = CreatePlan();
        var badDuration = plan.PlannedVideoDuration - TimeSpan.FromSeconds(1);
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsDurationCorrectionInvocation(request))
            {
                File.WriteAllBytes(request.Arguments[^1], []);
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        // Every VerifyAsync call reports the same out-of-tolerance duration -
        // even the guaranteed-effective remux "doesn't help" in this fake,
        // proving the loop still terminates (bounded, per CLAUDE.md rule 6)
        // and surfaces the failure rather than retrying forever.
        var verifier = CreateSequencedDurationOnlyVerifier(processRunner, badDuration);
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), verifier, AlwaysSufficientResourceGovernor);

        var exception = await Assert.ThrowsAsync<RenderVerificationException>(
            () => service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None));

        Assert.False(exception.Result.DurationWithinTolerance);
        Assert.Equal(3, processRunner.Requests.Count(IsRenderInvocation));
        Assert.Single(processRunner.Requests, IsDurationCorrectionInvocation);
    }

    [Fact]
    public async Task RenderAsync_NonDurationVerificationFailure_NeverAttemptsCorrection_ThrowsImmediately()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero }));
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreateFailingVerifier(processRunner), AlwaysSufficientResourceGovernor);

        await Assert.ThrowsAsync<RenderVerificationException>(
            () => service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None));

        // A failure that ALSO fails a content check (here: wrong audio-stream
        // count) is not duration-only - no correction tier should ever run.
        Assert.Equal(1, processRunner.Requests.Count(IsRenderInvocation));
        Assert.DoesNotContain(processRunner.Requests, IsDurationCorrectionInvocation);
    }
}
