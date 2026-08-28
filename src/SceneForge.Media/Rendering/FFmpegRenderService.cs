using System.Diagnostics;
using System.Globalization;
using System.Text;
using SceneForge.Core.Resources;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Rendering.Internal;
using SceneForge.Media.Tooling;
using SceneForge.Media.Validation;

namespace SceneForge.Media.Rendering;

// Renders a RenderPlan to a concrete file: selects an encoder by capability
// testing (never a GPU name lookup - see HardwareEncoderProbe), builds one
// filter_complex graph that trims/normalizes/concatenates every segment and
// mutes/replaces the audio track in a single ffmpeg invocation (falling
// back to a temporary filter script file when the graph would be too long
// for a safe Windows command line - see BuildFilterArguments), streams
// machine-readable progress from ffmpeg's own '-progress' output, and
// verifies the result via RenderOutputVerifier before returning.
public sealed class FFmpegRenderService : IFFmpegRenderService
{
    // ffmpeg's own command line is invoked without a shell (ProcessRunner
    // always uses ArgumentList, never string concatenation), so the binding
    // constraint is Win32 CreateProcess's ~32,767 wide-character total
    // command line limit, not cmd.exe's much smaller 8,191 limit. This
    // threshold on the filter_complex string ALONE (the dominant
    // contributor once segment counts grow) leaves generous headroom for
    // both file paths, the encoder/audio arguments, and process overhead -
    // the bounded intermediate strategy the phase brief asks for is to
    // write the graph to a temporary script file (deleted after the
    // process exits, always, via the finally block below) and read the
    // filter graph back from that file once the inline graph would risk
    // that limit.
    //
    // The file is passed with ffmpeg's generic "read this option's value
    // from a file" syntax, '-/filter_complex <path>' (available since
    // ffmpeg 7.0, 2024-04). The older dedicated '-filter_complex_script'
    // option was deprecated in 7.0 and REMOVED in 8.0 - passing it to a
    // current ffmpeg fails the whole invocation before any work starts
    // with "Unrecognized option 'filter_complex_script'. Error splitting
    // the argument list: Option not found". FfmpegToolLocator already
    // rejects tools that do not identify as ffmpeg; SceneForge ships/expects
    // a modern build, so '-/filter_complex' is the safe form here.
    internal const int InlineFilterGraphCharacterThreshold = 6_000;

    // ffmpeg's generic "load this option's value from a file" prefix form.
    // '-/filter_complex <file>' is exactly equivalent to spelling the whole
    // graph out inline after '-filter_complex', without the command-line
    // length exposure. Replaces the removed '-filter_complex_script'.
    internal const string FilterComplexFromFileOption = "-/filter_complex";
    internal const string InlineFilterComplexOption = "-filter_complex";

    // A single filter_complex graph carries roughly seven libavfilter nodes
    // per segment (trim/setpts/scale/pad/fps/format/setsar) PLUS one
    // implicit split output and one concat input per segment, all allocated
    // at once and all fed from a single decoded input. Phase 16's
    // never-short-output guarantee - combined with any long audio target -
    // pushes segment counts into the many hundreds regardless of how much
    // the plan repeats: 19 clips / ~67s against a 22-minute target produces
    // 378 placements (heavy repetition); a source with plenty of clean
    // footage against the same target produces ~330 placements with NO
    // repetition. Both build an ~70,000-80,000-character, ~2,300-2,650-node
    // filtergraph that ffmpeg 9.x fails to allocate ("Cannot allocate
    // memory").
    //
    // This is the *initial guess* at how many segments one filter_complex
    // can carry, not a hard limit: how much memory a graph of a given size
    // needs varies by machine, resolution, and what else is running, so
    // there is no single safe number to hardcode. The batched strategy
    // starts here and then SELF-CORRECTS - any batch ffmpeg fails to
    // allocate is automatically re-rendered as two smaller batches,
    // recursively, down to one segment if that is what the machine needs
    // (see RenderSegmentRunAsync / RenderBatchSplitEvent). The single-pass
    // path is likewise not trusted blindly: a single-pass render that fails
    // with a memory error falls through to the same adaptive batched path
    // (see TryRenderOnceAsync). So the render never needs to know in advance
    // "how many is too many" - it discovers the working size every time, on
    // whatever machine it is running on.
    //
    // Plans at or below this many segments start on the single-pass graph;
    // above it, on the batched pre-render (both then adapt as above). Two
    // piece-production strategies feed the same concat-demuxer assembly (see
    // SelectRenderStrategy):
    //   - DistinctDedup: render each DISTINCT segment window once. Optimal
    //     when the plan repeats a small set (pre-render volume stays far
    //     below the output duration). Each piece is one segment, already
    //     minimal - nothing to split.
    //   - Batched: render the placement sequence in consecutive batches
    //     starting at this size, halving on any memory failure. The general
    //     guarantee - correct for any total/distinct mix and any machine.
    internal const int InitialBatchSegmentCount = 60;

    // DistinctDedup is preferred over Batched only when distinct segments
    // are at most this fraction of total segments (every distinct window
    // reused ~2x+ on average) AND the distinct count itself still fits a
    // reasonable number of one-shot encodes (MaxDistinctDedupPieces) - so
    // the dedup pre-render stays bounded well below the output duration
    // rather than becoming a per-segment re-encode of the whole timeline.
    // Otherwise Batched is used. Either way the plan always renders
    // (CLAUDE.md rule 15 / the audio-duration guarantee).
    internal const double MaxDistinctToTotalRatioForDedup = 0.5;
    internal const int MaxDistinctDedupPieces = 400;

    // ffmpeg's concat demuxer needs a plain-text list of files, one per
    // line as "file '<path>'", with a literal single quote in a path
    // escaped as '\'' (the demuxer's own quoting rule).
    internal const string ConcatListFileName = "segments.txt";

    internal enum RenderStrategy
    {
        // One filter_complex graph for the whole plan (small plans only).
        SinglePass,

        // Pre-render each distinct segment window once, then concat-demux
        // one entry per placement.
        DistinctDedup,

        // Pre-render the placement sequence in batches (starting at
        // InitialBatchSegmentCount, halved on any memory failure), then
        // concat-demux one entry per rendered piece.
        Batched,
    }

    // A deliberately conservative, disclosed *estimate* - not a guarantee -
    // of encoded output size, used only to fail fast with a clear error
    // before spending minutes re-encoding into a destination that was never
    // going to fit. ~1 MB/s covers typical 1080p H.264 CRF output with
    // headroom; RenderOutputVerifier still does the real, authoritative
    // post-render validation regardless of what this guard predicted.
    private const long EstimatedBytesPerSecondOfOutput = 1_000_000;
    private const long MinimumRequiredFreeBytes = 200_000_000;

    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly IHardwareEncoderProbe _encoderProbe;
    private readonly RenderOutputVerifier _verifier;
    private readonly IAdaptiveResourceGovernor _resourceGovernor;

    public FFmpegRenderService(IProcessRunner processRunner, IFfmpegToolLocator toolLocator, IFfprobeService ffprobeService, IAdaptiveResourceGovernor resourceGovernor)
        : this(processRunner, toolLocator, new HardwareEncoderProbe(processRunner, toolLocator), new RenderOutputVerifier(ffprobeService, processRunner, toolLocator), resourceGovernor)
    {
    }

    internal FFmpegRenderService(
        IProcessRunner processRunner,
        IFfmpegToolLocator toolLocator,
        IHardwareEncoderProbe encoderProbe,
        RenderOutputVerifier verifier,
        IAdaptiveResourceGovernor resourceGovernor)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(toolLocator);
        ArgumentNullException.ThrowIfNull(encoderProbe);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(resourceGovernor);

        _processRunner = processRunner;
        _toolLocator = toolLocator;
        _encoderProbe = encoderProbe;
        _verifier = verifier;
        _resourceGovernor = resourceGovernor;
    }

    public async Task<RenderResult> RenderAsync(
        RenderPlan plan,
        string outputFilePath,
        IProgress<RenderProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            throw new ArgumentException("An output file path is required.", nameof(outputFilePath));
        }

        var outputDirectory = OutputDirectoryValidator.EnsureWritable(Path.GetDirectoryName(Path.GetFullPath(outputFilePath)) ?? ".");
        var resolvedOutputPath = Path.Combine(outputDirectory, Path.GetFileName(outputFilePath));
        OutputDirectoryValidator.EnsureDoesNotOverwriteInput(resolvedOutputPath, plan.SourceFilePath);
        OutputDirectoryValidator.EnsureDoesNotOverwriteInput(resolvedOutputPath, plan.Audio.FilePath);

        var estimatedRequiredBytes = Math.Max(MinimumRequiredFreeBytes, (long)(plan.PlannedVideoDuration.TotalSeconds * EstimatedBytesPerSecondOfOutput));
        _resourceGovernor.EnsureSufficientDiskSpace(outputDirectory, estimatedRequiredBytes);

        var tools = await _toolLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
        var encoder = await _encoderProbe.SelectEncoderAsync(cancellationToken).ConfigureAwait(false);

        // Make the capability-probe outcome visible: which encoder won, and
        // whether it is hardware-accelerated. Every Stage A ffmpeg in the
        // batched/dedup path uses exactly this -c:v (see BuildSegmentRunArguments);
        // a silent fall-through to software is the difference between a
        // multi-minute and a multi-hour render at scale, so it is logged
        // rather than left to be inferred from RenderResult.Encoder (which
        // still carries the authoritative record for callers).
        var encoderAccel = encoder.IsHardwareAccelerated ? "hardware-accelerated" : "software";
        Trace.WriteLine(FormattableString.Invariant(
            $"[SceneForge.Render] encoder probe selected '{encoder.FfmpegEncoderName}' ({encoderAccel}); strategy {SelectRenderStrategy(plan)}, {plan.Segments.Count} segment(s)"));

        var stopwatch = Stopwatch.StartNew();
        var (fellBack, usedEncoder, splitEvents) = await RunWithFallbackAsync(tools.FfmpegPath, plan, resolvedOutputPath, encoder, progress, stopwatch, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        var verification = await _verifier.VerifyAsync(resolvedOutputPath, plan, cancellationToken).ConfigureAwait(false);
        if (!verification.IsValid)
        {
            throw new RenderVerificationException(verification);
        }

        return new RenderResult
        {
            OutputFilePath = resolvedOutputPath,
            Encoder = usedEncoder,
            FellBackToSoftwareEncoder = fellBack,
            Elapsed = stopwatch.Elapsed,
            Verification = verification,
            BatchSplitEvents = splitEvents,
        };
    }

    // What one full render attempt (one strategy, one encoder, including any
    // internal adaptive batch splitting) produced.
    private sealed record RenderAttemptOutcome
    {
        public required bool Success { get; init; }

        public int ExitCode { get; init; }

        public string ErrorExcerpt { get; init; } = string.Empty;

        // True when the failure was ffmpeg reporting an out-of-memory /
        // allocation error (see IsProbableMemoryFailure) - the signal the
        // single-pass path uses to fall through to adaptive batching rather
        // than to the encoder fallback.
        public bool FailedForMemory { get; init; }

        public IReadOnlyList<RenderBatchSplitEvent> SplitEvents { get; init; } = [];
    }

    private async Task<(bool FellBack, VideoEncoderSelection Encoder, IReadOnlyList<RenderBatchSplitEvent> SplitEvents)> RunWithFallbackAsync(
        string ffmpegPath,
        RenderPlan plan,
        string outputFilePath,
        VideoEncoderSelection encoder,
        IProgress<RenderProgress>? progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var attemptResult = await TryRenderOnceAsync(ffmpegPath, plan, outputFilePath, encoder, progress, stopwatch, cancellationToken).ConfigureAwait(false);
        if (attemptResult.Success)
        {
            return (false, encoder, attemptResult.SplitEvents);
        }

        if (!encoder.IsHardwareAccelerated)
        {
            throw new RenderExecutionException(
                $"ffmpeg render with encoder '{encoder.FfmpegEncoderName}' failed (exit code {attemptResult.ExitCode}): {attemptResult.ErrorExcerpt}");
        }

        // Hardware output must be validated and fall back safely: the
        // encoder passed its own short capability smoke test, but a real,
        // full-length render can still fail (unsupported resolution/
        // profile, driver contention, VRAM exhaustion). Retry once, in
        // full, with the best available software encoder rather than
        // surfacing a hardware-specific failure to the caller. The software
        // encoder name is resolved by the probe, never hardcoded: SceneForge's
        // own vendored ffmpeg is built --disable-libx264, so on it the
        // fallback must resolve to libopenh264 - a hardcoded "libx264" here
        // would turn every hardware-render failure into a hard error instead
        // of a slow-but-working render. (A pure out-of-memory failure is
        // already handled inside TryRenderOnceAsync by adaptive batch
        // splitting, with the same encoder - it only reaches here if
        // splitting all the way down still could not allocate, which a
        // software encoder will not fix either, but retrying once is harmless
        // and keeps this path uniform.)
        var softwareEncoder = await _encoderProbe.SelectSoftwareEncoderAsync(cancellationToken).ConfigureAwait(false);
        Trace.WriteLine(FormattableString.Invariant(
            $"[SceneForge.Render] hardware encoder '{encoder.FfmpegEncoderName}' render failed (exit {attemptResult.ExitCode}); retrying the whole render with software encoder '{softwareEncoder.FfmpegEncoderName}'"));

        var fallbackResult = await TryRenderOnceAsync(ffmpegPath, plan, outputFilePath, softwareEncoder, progress, stopwatch, cancellationToken).ConfigureAwait(false);
        if (!fallbackResult.Success)
        {
            throw new RenderExecutionException(
                $"ffmpeg render failed with hardware encoder '{encoder.FfmpegEncoderName}' (exit code {attemptResult.ExitCode}: {attemptResult.ErrorExcerpt}) " +
                $"and the '{softwareEncoder.FfmpegEncoderName}' software fallback also failed (exit code {fallbackResult.ExitCode}: {fallbackResult.ErrorExcerpt}).");
        }

        return (true, softwareEncoder, fallbackResult.SplitEvents);
    }

    private async Task<RenderAttemptOutcome> TryRenderOnceAsync(
        string ffmpegPath,
        RenderPlan plan,
        string outputFilePath,
        VideoEncoderSelection encoder,
        IProgress<RenderProgress>? progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var strategy = SelectRenderStrategy(plan);

        if (strategy == RenderStrategy.SinglePass)
        {
            var singlePass = await RenderSinglePassAsync(ffmpegPath, plan, outputFilePath, encoder, progress, stopwatch, cancellationToken).ConfigureAwait(false);
            if (singlePass.Success || !singlePass.FailedForMemory)
            {
                return singlePass;
            }

            // The single filter_complex graph could not be allocated on this
            // machine even at this (small) segment count. Fall through to the
            // adaptive batched path, which will shrink the per-ffmpeg graph
            // until it fits.
            var message = $"single-pass render hit a memory limit at {plan.Segments.Count} segments; retrying with adaptive batching";
            Trace.WriteLine($"[SceneForge.Render] {message}");
            progress?.Report(NonTimelineProgress(stopwatch, $"Reducing render batch size after a memory limit ({plan.Segments.Count} segments)"));
            strategy = RenderStrategy.Batched;
        }

        return strategy == RenderStrategy.DistinctDedup
            ? await RenderViaConcatDemuxerAsync(RenderStrategy.DistinctDedup, ffmpegPath, plan, outputFilePath, encoder, progress, stopwatch, cancellationToken).ConfigureAwait(false)
            : await RenderViaConcatDemuxerAsync(RenderStrategy.Batched, ffmpegPath, plan, outputFilePath, encoder, progress, stopwatch, cancellationToken).ConfigureAwait(false);
    }

    private static RenderProgress NonTimelineProgress(Stopwatch stopwatch, string statusMessage) => new()
    {
        FrameNumber = 0,
        OutTime = TimeSpan.Zero,
        Elapsed = stopwatch.Elapsed,
        IsFinished = false,
        StatusMessage = statusMessage,
    };

    // ffmpeg reports an out-of-memory condition on stderr (its ENOMEM
    // strerror is "Cannot allocate memory"); a hard Win32 STATUS_NO_MEMORY
    // shows up as that exit code. Either is the signal to render smaller,
    // not to fail or to switch encoders.
    internal static bool IsProbableMemoryFailure(int exitCode, string standardError)
    {
        if (exitCode == unchecked((int)0xC0000017)) // STATUS_NO_MEMORY
        {
            return true;
        }

        return standardError.Contains("Cannot allocate memory", StringComparison.OrdinalIgnoreCase)
            || standardError.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
            || standardError.Contains("not enough memory", StringComparison.OrdinalIgnoreCase)
            || standardError.Contains("Failed to allocate", StringComparison.OrdinalIgnoreCase)
            || standardError.Contains("bad_alloc", StringComparison.OrdinalIgnoreCase);
    }

    // The strategy a plan STARTS on. Small plans render in one
    // filter_complex pass (and fall through to Batched if that hits a
    // memory limit - see TryRenderOnceAsync). Larger plans are pre-rendered
    // in pieces and assembled with the concat demuxer: DistinctDedup when
    // the plan repeats a small window set cheaply enough, Batched otherwise
    // (the general path - correct for any total/distinct mix, and it shrinks
    // its own batch size on any memory failure). See InitialBatchSegmentCount
    // / MaxDistinctToTotalRatioForDedup / MaxDistinctDedupPieces.
    internal static RenderStrategy SelectRenderStrategy(RenderPlan plan)
    {
        if (plan.Segments.Count <= InitialBatchSegmentCount)
        {
            return RenderStrategy.SinglePass;
        }

        var distinctSegmentCount = CountDistinctSegments(plan.Segments);
        var repetitionIsWorthDeduping = distinctSegmentCount <= plan.Segments.Count * MaxDistinctToTotalRatioForDedup;
        var dedupPieceCountIsBounded = distinctSegmentCount <= MaxDistinctDedupPieces;

        return repetitionIsWorthDeduping && dedupPieceCountIsBounded
            ? RenderStrategy.DistinctDedup
            : RenderStrategy.Batched;
    }

    // A segment's normalized video output is fully determined by its trim
    // window (SourceStart/SourceDuration) - rotation and OutputSpec are
    // constant across the whole plan - so two segments with the same window
    // produce byte-identical pre-rendered files and only need encoding once.
    private static int CountDistinctSegments(IReadOnlyList<RenderSegment> segments)
    {
        var seen = new HashSet<(TimeSpan Start, TimeSpan Duration)>();
        foreach (var segment in segments)
        {
            seen.Add((segment.SourceStart, segment.SourceDuration));
        }

        return seen.Count;
    }

    private async Task<RenderAttemptOutcome> RenderSinglePassAsync(
        string ffmpegPath,
        RenderPlan plan,
        string outputFilePath,
        VideoEncoderSelection encoder,
        IProgress<RenderProgress>? progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        string? scriptFilePath = null;
        try
        {
            var filterGraph = RenderFilterGraphBuilder.Build(plan);
            var filterArguments = BuildFilterArguments(filterGraph, out scriptFilePath);
            var arguments = BuildArguments(plan, outputFilePath, encoder, filterArguments);

            var parser = new RenderProgressParser();
            var outputProgress = progress is null
                ? null
                : new SynchronousProgress<ProcessOutputLine>(line =>
                {
                    if (line.Channel != ProcessOutputChannel.StandardOutput)
                    {
                        return;
                    }

                    var update = parser.Accept(line.Text, stopwatch.Elapsed);
                    if (update is null)
                    {
                        return;
                    }

                    progress.Report(WithEstimatedTimeRemaining(update, plan.PlannedVideoDuration));
                });

            var result = await _processRunner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    OutputProgress = outputProgress,
                },
                cancellationToken).ConfigureAwait(false);

            return new RenderAttemptOutcome
            {
                Success = result.ExitCode == 0,
                ExitCode = result.ExitCode,
                ErrorExcerpt = Excerpt(result.StandardError),
                FailedForMemory = result.ExitCode != 0 && IsProbableMemoryFailure(result.ExitCode, result.StandardError),
            };
        }
        finally
        {
            if (scriptFilePath is not null)
            {
                TryDeleteFile(scriptFilePath);
            }
        }
    }

    // The scalable render path for any plan past InitialBatchSegmentCount,
    // and the fallback for a single-pass render that hit a memory limit.
    // Two stages, sharing the same concat-demuxer assembly regardless of how
    // the pre-rendered pieces were produced:
    //
    //   Stage A - encode the timeline into intermediate video-only files,
    //     each built from a filter_complex whose size ffmpeg can actually
    //     allocate on this machine (discovered by retry - see below):
    //       * DistinctDedup: one piece per DISTINCT segment window, encoded
    //         once and referenced by every placement that uses it. Optimal
    //         when the plan repeats a small set - the pre-render volume
    //         stays far below the output duration. Each piece is one
    //         segment, already minimal.
    //       * Batched: the placement sequence in consecutive batches
    //         starting at InitialBatchSegmentCount. Any batch ffmpeg fails
    //         to allocate ("Cannot allocate memory") is automatically
    //         re-rendered as two smaller batches, recursively, down to one
    //         segment if that is what the machine needs
    //         (RenderSegmentRunAsync). Every split is recorded on
    //         RenderResult.BatchSplitEvents and written to
    //         System.Diagnostics.Trace. The general path - correct for any
    //         total/distinct mix and any machine, with no hardcoded ceiling
    //         on segment count.
    //
    //   Stage B - list the pieces in playback order for ffmpeg's concat
    //     demuxer, stream-copy the assembled video (no re-encode of the full
    //     timeline), and trim/encode the supplied audio track in the same
    //     pass. The demuxer opens files in sequence, so this scales to any
    //     piece count - no giant filtergraph, no N-way split.
    //
    // Every intermediate (piece files, per-piece filter scripts, the list
    // file) lives under one temp directory deleted in the finally block on
    // every exit path. The pre-render's decoded-frame working set is bounded
    // by one batch at a time; the encoded piece files are the output written
    // in bounded chunks, disk-space-checked up front - not a decoded-video
    // buffer (CLAUDE.md rule 7). The plan always renders regardless of
    // segment count or repetition mix (CLAUDE.md rule 15 / the
    // audio-duration guarantee).
    private async Task<RenderAttemptOutcome> RenderViaConcatDemuxerAsync(
        RenderStrategy strategy,
        string ffmpegPath,
        RenderPlan plan,
        string outputFilePath,
        VideoEncoderSelection encoder,
        IProgress<RenderProgress>? progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "SceneForge", "render-concat", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var context = new StageAContext
        {
            FfmpegPath = ffmpegPath,
            Plan = plan,
            Encoder = encoder,
            WorkingDirectory = workingDirectory,
            Progress = progress,
            Stopwatch = stopwatch,
            SplitEvents = new List<RenderBatchSplitEvent>(),
        };

        try
        {
            // A safe upper bound on total intermediate size for both
            // strategies: batched writes ~the whole output once, dedup less.
            _resourceGovernor.EnsureSufficientDiskSpace(
                workingDirectory,
                Math.Max(MinimumRequiredFreeBytes, (long)(plan.PlannedVideoDuration.TotalSeconds * EstimatedBytesPerSecondOfOutput)));

            var (playbackOrder, stageAFailure) = strategy == RenderStrategy.DistinctDedup
                ? await RenderDistinctDedupStageAAsync(context, cancellationToken).ConfigureAwait(false)
                : await RenderBatchedStageAAsync(context, cancellationToken).ConfigureAwait(false);

            if (stageAFailure is not null)
            {
                return stageAFailure;
            }

            // Stage B: concat demuxer assembles the final file.
            var listFilePath = Path.Combine(workingDirectory, ConcatListFileName);
            await File.WriteAllTextAsync(listFilePath, BuildConcatListFileContent(playbackOrder!), cancellationToken).ConfigureAwait(false);

            var parser = new RenderProgressParser();
            var outputProgress = progress is null
                ? null
                : new SynchronousProgress<ProcessOutputLine>(line =>
                {
                    if (line.Channel != ProcessOutputChannel.StandardOutput)
                    {
                        return;
                    }

                    var update = parser.Accept(line.Text, stopwatch.Elapsed);
                    if (update is null)
                    {
                        return;
                    }

                    // Map the copy pass's real output time into the tail
                    // [StageAProgressShare, 1] of the overall bar.
                    var copyFraction = plan.PlannedVideoDuration > TimeSpan.Zero
                        ? Math.Clamp(update.OutTime.TotalSeconds / plan.PlannedVideoDuration.TotalSeconds, 0, 1)
                        : 1.0;
                    var overall = StageAProgressShare + (1 - StageAProgressShare) * copyFraction;
                    var mapped = update with { OutTime = ScaleDuration(plan.PlannedVideoDuration, overall) };
                    progress.Report(WithEstimatedTimeRemaining(mapped, plan.PlannedVideoDuration));
                });

            var stageBResult = await _processRunner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = ffmpegPath,
                    Arguments = BuildStageBConcatArguments(plan, listFilePath, outputFilePath),
                    OutputProgress = outputProgress,
                },
                cancellationToken).ConfigureAwait(false);

            return new RenderAttemptOutcome
            {
                Success = stageBResult.ExitCode == 0,
                ExitCode = stageBResult.ExitCode,
                ErrorExcerpt = Excerpt(stageBResult.StandardError),
                FailedForMemory = stageBResult.ExitCode != 0 && IsProbableMemoryFailure(stageBResult.ExitCode, stageBResult.StandardError),
                SplitEvents = context.SplitEvents,
            };
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    // Mutable working state for one Stage A run - avoids threading a dozen
    // parameters through the recursive batch splitter.
    private sealed class StageAContext
    {
        public required string FfmpegPath { get; init; }

        public required RenderPlan Plan { get; init; }

        public required VideoEncoderSelection Encoder { get; init; }

        public required string WorkingDirectory { get; init; }

        public IProgress<RenderProgress>? Progress { get; init; }

        public required Stopwatch Stopwatch { get; init; }

        public required List<RenderBatchSplitEvent> SplitEvents { get; init; }

        // Next intermediate-file index; incremented for every ffmpeg piece
        // attempt (including sub-batches produced by a split) so file names
        // and per-piece filter-script names never collide within a run.
        public int PieceCounter;
    }

    // DistinctDedup Stage A: render each distinct (SourceStart, SourceDuration)
    // window once, then map every placement back to its window's file.
    private async Task<(IReadOnlyList<string>? PlaybackOrder, RenderAttemptOutcome? Failure)> RenderDistinctDedupStageAAsync(
        StageAContext context,
        CancellationToken cancellationToken)
    {
        var fileByWindow = new Dictionary<(TimeSpan Start, TimeSpan Duration), string>();
        var producedFiles = new List<string>();
        var distinctWindows = context.Plan.Segments
            .Select(s => (s.SourceStart, s.SourceDuration))
            .Distinct()
            .ToList();

        for (var i = 0; i < distinctWindows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var window = distinctWindows[i];
            var sample = context.Plan.Segments.First(s => (s.SourceStart, s.SourceDuration) == window);

            // A single-segment run: it renders one file and can never split.
            var failure = await RenderSegmentRunAsync(context, [sample], depth: 0, producedFiles, cancellationToken).ConfigureAwait(false);
            if (failure is not null)
            {
                return (null, failure);
            }

            fileByWindow[window] = producedFiles[^1];
            ReportStageAProgress(context, (i + 1) / (double)distinctWindows.Count);
        }

        var playbackOrder = context.Plan.Segments
            .Select(s => fileByWindow[(s.SourceStart, s.SourceDuration)])
            .ToList();

        return (playbackOrder, null);
    }

    // Batched Stage A: render the placement sequence in consecutive batches
    // starting at InitialBatchSegmentCount, halving any batch ffmpeg cannot
    // allocate. The playback order is simply the successful piece files in
    // the order they were produced.
    private async Task<(IReadOnlyList<string>? PlaybackOrder, RenderAttemptOutcome? Failure)> RenderBatchedStageAAsync(
        StageAContext context,
        CancellationToken cancellationToken)
    {
        var topBatches = context.Plan.Segments.Chunk(InitialBatchSegmentCount).ToList();
        var producedFiles = new List<string>();

        for (var b = 0; b < topBatches.Count; b++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var failure = await RenderSegmentRunAsync(context, topBatches[b], depth: 0, producedFiles, cancellationToken).ConfigureAwait(false);
            if (failure is not null)
            {
                return (null, failure);
            }

            ReportStageAProgress(context, (b + 1) / (double)topBatches.Count);
        }

        return (producedFiles, null);
    }

    // Renders one contiguous run of segments into a single intermediate file
    // (appended to producedFiles). On an ffmpeg out-of-memory failure with
    // more than one segment, splits the run in half and renders each half
    // the same way, recursively - so the effective per-ffmpeg batch size
    // shrinks until it fits whatever memory this machine has right now,
    // down to one segment. Returns null on success, or a failure outcome if
    // the run cannot be rendered even at one segment / the failure was not a
    // memory failure. Every split is recorded and logged.
    private async Task<RenderAttemptOutcome?> RenderSegmentRunAsync(
        StageAContext context,
        IReadOnlyList<RenderSegment> segments,
        int depth,
        List<string> producedFiles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pieceIndex = context.PieceCounter++;
        var pieceFile = Path.Combine(context.WorkingDirectory, $"piece-{pieceIndex:D5}.mkv");
        var result = await _processRunner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = context.FfmpegPath,
                Arguments = BuildSegmentRunArguments(context.Plan, segments, context.Encoder, context.WorkingDirectory, pieceIndex, pieceFile),
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode == 0)
        {
            producedFiles.Add(pieceFile);
            return null;
        }

        var isMemoryFailure = IsProbableMemoryFailure(result.ExitCode, result.StandardError);

        if (isMemoryFailure && segments.Count > 1)
        {
            var mid = segments.Count / 2;
            var firstHalf = segments.Take(mid).ToArray();
            var secondHalf = segments.Skip(mid).ToArray();

            context.SplitEvents.Add(new RenderBatchSplitEvent
            {
                SegmentCount = segments.Count,
                FirstHalfSegmentCount = firstHalf.Length,
                SecondHalfSegmentCount = secondHalf.Length,
                Depth = depth,
                FfmpegErrorExcerpt = Excerpt(result.StandardError),
            });

            var message = FormattableString.Invariant(
                $"ffmpeg could not allocate a {segments.Count}-segment render batch (depth {depth}); retrying as {firstHalf.Length} + {secondHalf.Length} segments");
            Trace.WriteLine($"[SceneForge.Render] {message}");
            context.Progress?.Report(NonTimelineProgress(
                context.Stopwatch,
                FormattableString.Invariant($"Reducing render batch size after a memory limit: {segments.Count} -> {firstHalf.Length}+{secondHalf.Length} segments")));

            var firstFailure = await RenderSegmentRunAsync(context, firstHalf, depth + 1, producedFiles, cancellationToken).ConfigureAwait(false);
            if (firstFailure is not null)
            {
                return firstFailure;
            }

            return await RenderSegmentRunAsync(context, secondHalf, depth + 1, producedFiles, cancellationToken).ConfigureAwait(false);
        }

        // Unrecoverable here: either not a memory failure (a real ffmpeg
        // error - corrupt source, bad args), or already down to one segment
        // and still out of memory. Surface it; RunWithFallbackAsync decides
        // whether a libx264 retry is worth attempting.
        if (isMemoryFailure)
        {
            Trace.WriteLine($"[SceneForge.Render] ffmpeg still could not allocate a single-segment render batch (exit {result.ExitCode})");
        }

        return new RenderAttemptOutcome
        {
            Success = false,
            ExitCode = result.ExitCode,
            ErrorExcerpt = Excerpt(result.StandardError),
            FailedForMemory = isMemoryFailure,
            SplitEvents = context.SplitEvents,
        };
    }

    private static void ReportStageAProgress(StageAContext context, double fraction) =>
        context.Progress?.Report(new RenderProgress
        {
            FrameNumber = 0,
            OutTime = ScaleDuration(context.Plan.PlannedVideoDuration, fraction * StageAProgressShare),
            Elapsed = context.Stopwatch.Elapsed,
            IsFinished = false,
        });

    // Fraction of the progress bar attributed to Stage A (the piece
    // encodes); the remainder is the Stage B stream copy.
    private const double StageAProgressShare = 0.95;

    private static TimeSpan ScaleDuration(TimeSpan value, double fraction) =>
        TimeSpan.FromTicks((long)Math.Clamp(value.Ticks * fraction, 0, value.Ticks));

    // Stage A: trim this run's segments from the source, normalize each to
    // OutputSpec exactly as the single-pass graph would, concatenate them,
    // and encode to a video-only intermediate. Pinned to the sum of the
    // segments' frame-quantized lengths via -frames:v so the concatenated
    // total is frame-exact against PlannedVideoDuration (see RenderPlanBuilder's
    // per-segment quantization). Audio is dropped here (-an); it is muxed in
    // Stage B. The filter graph is written to a script file inside
    // workingDirectory when it exceeds the inline command-line threshold.
    //
    // Each segment gets its OWN '-ss <SourceStart> -i <source>' input rather
    // than one shared '-i' fanned out with split. The input-level seek makes
    // ffmpeg decode ~one GOP into each segment instead of the whole source
    // from frame 0 for the batch: a Position-ordered batch's segments are
    // scattered across the source (the planner shuffles), so a single shared
    // decode reads almost the entire source for every batch - measured at
    // ~30x the cost of a seeked read for a segment near the end of an
    // 8-minute source, and it compounds with every additional batch. Nothing
    // about batch sizing or the out-of-memory / halving retry changes here:
    // a batch is still <= InitialBatchSegmentCount segments and still splits
    // in half on an allocation failure (RenderSegmentRunAsync) - the run
    // just carries N seeked inputs now instead of one, which the seeked
    // concat graph (RenderFilterGraphBuilder.BuildSeekedVideoConcat) reads
    // as [k:v] per segment. At the <=60-segment batch cap the extra '-ss
    // <n> -i <path>' tokens add a few KB to the command line, well inside
    // the Win32 limit the InlineFilterGraphCharacterThreshold note covers.
    private static List<string> BuildSegmentRunArguments(
        RenderPlan plan,
        IReadOnlyList<RenderSegment> segments,
        VideoEncoderSelection encoder,
        string workingDirectory,
        int pieceIndex,
        string pieceFile)
    {
        var spec = plan.OutputSpec;
        var frameRate = $"{spec.FrameRate.Numerator}/{spec.FrameRate.Denominator}";
        var frameCount = segments.Sum(s => Math.Max(1, spec.FrameRate.ToFrameCount(s.SourceDuration)));
        var graph = RenderFilterGraphBuilder.BuildSeekedVideoConcat(segments, spec, plan.SourceRotationDegrees);

        var arguments = new List<string> { "-hide_banner", "-y", "-loglevel", "error" };
        foreach (var segment in segments)
        {
            arguments.Add("-ss");
            arguments.Add(FormatSeconds(segment.SourceStart));
            arguments.Add("-i");
            arguments.Add(plan.SourceFilePath);
        }

        arguments.AddRange(BuildPieceFilterArguments(graph, workingDirectory, pieceIndex));
        arguments.AddRange(["-map", RenderFilterGraphBuilder.VideoOutputLabel, "-c:v", encoder.FfmpegEncoderName]);
        arguments.AddRange(EncoderQualityArguments(encoder.Kind));
        arguments.AddRange(["-pix_fmt", spec.PixelFormat, "-r", frameRate]);
        arguments.AddRange(["-frames:v", frameCount.ToString(CultureInfo.InvariantCulture), "-an", pieceFile]);
        return arguments;
    }

    // Seconds with microsecond precision, invariant culture - the same
    // format RenderFilterGraphBuilder uses for trim offsets, so a segment's
    // '-ss' value and its (post-seek, zero-based) trim window are expressed
    // consistently.
    private static string FormatSeconds(TimeSpan value) =>
        value.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture);

    private static string[] BuildPieceFilterArguments(string filterGraph, string workingDirectory, int pieceIndex)
    {
        if (filterGraph.Length <= InlineFilterGraphCharacterThreshold)
        {
            return [InlineFilterComplexOption, filterGraph];
        }

        var scriptPath = Path.Combine(workingDirectory, $"filter-{pieceIndex:D5}.txt");
        File.WriteAllText(scriptPath, filterGraph);
        return [FilterComplexFromFileOption, scriptPath];
    }

    // Stage B: hand the concat demuxer the ordered piece list, stream-copy
    // the assembled video, and trim/encode the supplied audio track in the
    // same pass. No -map of 0:a anywhere - the concat input carries only the
    // pre-rendered (audio-free) video pieces.
    private static List<string> BuildStageBConcatArguments(RenderPlan plan, string listFilePath, string outputFilePath)
    {
        var arguments = new List<string>
        {
            "-hide_banner", "-y", "-loglevel", "error",
            "-f", "concat", "-safe", "0", "-i", listFilePath,
            "-i", plan.Audio.FilePath,
            InlineFilterComplexOption, RenderFilterGraphBuilder.BuildAudioOnlyGraph(plan.Audio),
            "-map", "0:v:0", "-map", RenderFilterGraphBuilder.AudioOutputLabel,
            "-c:v", "copy",
            "-c:a", plan.Audio.Codec,
            "-ar", plan.Audio.SampleRateHz.ToString(CultureInfo.InvariantCulture),
            "-ac", plan.Audio.Channels.ToString(CultureInfo.InvariantCulture),
        };

        if (plan.Audio.BitRateBitsPerSecond is { } bitRate)
        {
            arguments.AddRange(["-b:a", $"{bitRate}"]);
        }

        arguments.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", outputFilePath]);
        return arguments;
    }

    private static string BuildConcatListFileContent(IReadOnlyList<string> orderedFiles)
    {
        var builder = new StringBuilder();
        foreach (var path in orderedFiles)
        {
            builder.Append("file '").Append(path.Replace("'", "'\\''", StringComparison.Ordinal)).Append('\'').Append('\n');
        }

        return builder.ToString();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; leftover pre-render segments under the OS
            // temp directory are not user data and clear on reboot.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    private static RenderProgress WithEstimatedTimeRemaining(RenderProgress update, TimeSpan plannedDuration)
    {
        if (update.Speed is not > 0)
        {
            return update;
        }

        var remainingOutput = plannedDuration - update.OutTime;
        if (remainingOutput <= TimeSpan.Zero)
        {
            return update with { EstimatedTimeRemaining = TimeSpan.Zero };
        }

        var eta = TimeSpan.FromSeconds(remainingOutput.TotalSeconds / update.Speed.Value);
        return update with { EstimatedTimeRemaining = eta };
    }

    private static string[] BuildFilterArguments(string filterGraph, out string? scriptFilePath)
    {
        if (filterGraph.Length <= InlineFilterGraphCharacterThreshold)
        {
            scriptFilePath = null;
            return [InlineFilterComplexOption, filterGraph];
        }

        var directory = Path.Combine(Path.GetTempPath(), "SceneForge", "render-filters");
        Directory.CreateDirectory(directory);
        scriptFilePath = Path.Combine(directory, $"{Guid.NewGuid():N}.filter");
        File.WriteAllText(scriptFilePath, filterGraph);
        return [FilterComplexFromFileOption, scriptFilePath];
    }

    private static List<string> BuildArguments(
        RenderPlan plan,
        string outputFilePath,
        VideoEncoderSelection encoder,
        IReadOnlyList<string> filterArguments)
    {
        var spec = plan.OutputSpec;
        var frameRate = $"{spec.FrameRate.Numerator}/{spec.FrameRate.Denominator}";

        var arguments = new List<string>
        {
            "-hide_banner", "-y", "-loglevel", "error",
            "-i", plan.SourceFilePath,
            "-i", plan.Audio.FilePath,
        };

        arguments.AddRange(filterArguments);
        arguments.AddRange(["-map", RenderFilterGraphBuilder.VideoOutputLabel, "-map", RenderFilterGraphBuilder.AudioOutputLabel]);
        arguments.AddRange(["-c:v", encoder.FfmpegEncoderName]);
        arguments.AddRange(EncoderQualityArguments(encoder.Kind));
        arguments.AddRange(["-pix_fmt", spec.PixelFormat, "-r", frameRate]);
        arguments.AddRange(["-c:a", plan.Audio.Codec, "-ar", plan.Audio.SampleRateHz.ToString(CultureInfo.InvariantCulture), "-ac", plan.Audio.Channels.ToString(CultureInfo.InvariantCulture)]);
        if (plan.Audio.BitRateBitsPerSecond is { } bitRate)
        {
            arguments.AddRange(["-b:a", $"{bitRate}"]);
        }

        arguments.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", outputFilePath]);
        return arguments;
    }

    // Conservative, documented-heuristic defaults per encoder - not tuned or
    // benchmarked against measured quality/size targets (CLAUDE.md rule 9
    // applies to optimizations; no baseline exists yet for encoder quality
    // tuning, see docs/PHASE_09_REPORT.md Outstanding). Shared with
    // HardwareEncoderProbe so the capability smoke test runs the exact
    // settings a real render will use.
    private static IReadOnlyList<string> EncoderQualityArguments(VideoEncoderKind kind) => EncoderQualityDefaults.For(kind);

    private static string Excerpt(string standardError)
    {
        const int maxLength = 2000;
        return standardError.Length <= maxLength ? standardError : standardError[^maxLength..];
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp filter script under the OS temp
            // directory is not user data and will be cleared on the next reboot.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
