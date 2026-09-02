using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using OpenCvSharp;
using SceneForge.Core.Resources;
using SceneForge.Media.Domain;
using SceneForge.Media.Probing;
using SceneForge.Media.Tooling;
using SceneForge.Media.Validation;

namespace SceneForge.Media.Sampling;

// Decodes a source file through ffmpeg into a downscaled raw BGR24/Gray8
// stream on stdout - never thousands of image files, never the source file
// buffered in memory or on disk (CLAUDE.md rule 7) - and correlates each
// output frame with its exact source timestamp via ffmpeg's `showinfo`
// filter log on stderr. Frames are read one at a time into ArrayPool
// buffers and handed to the caller through a bounded
// System.Threading.Channels channel, so a slow consumer applies real
// backpressure to the ffmpeg process (bounded memory/concurrency, rule 6)
// instead of the producer racing ahead and accumulating frames in memory.
//
// This is also the one composition point every OpenCV-consuming analysis
// stage (TransitionDetector's SignalPipeline, CleanClipExtractor's
// ClipFrameMetricsPipeline) depends on to source frames in the first place,
// so it is where the app's whole-machine CPU budget (
// IAdaptiveResourceGovernor.MaxWorkers) gets split and applied to BOTH
// halves of the concurrent decode/analyze pipeline it drives - see
// ApplyCpuBudget. Neither ffmpeg nor OpenCV bounds its own internal
// threading by default (both happily use every logical CPU), and the two
// run concurrently here (this class streams decoded frames through a
// bounded channel to a consumer while ffmpeg is still decoding further
// frames), so handing the FULL budget to each independently would let
// their combined usage run up to 2x over the cap. Splitting it once here
// means callers that construct a FrameSampler never have to remember to
// configure OpenCV themselves.
public sealed class FrameSampler : IFrameSampler
{
    // ffmpeg's share of the CPU budget for the decode + fps/showinfo/scale
    // filter chain here. Deliberately small and fixed rather than
    // proportional to MaxWorkers: this decode always targets an already
    // downscaled analysis-resolution stream (see FrameDimensions.
    // ForTargetWidth), so it is the cheap side of the concurrent
    // decode/analyze pair - the CPU-heavy work is the per-frame OpenCV
    // analysis on the consuming side (Farneback optical flow, Canny,
    // histogram math), which gets the rest of the budget. See
    // ApplyCpuBudget.
    internal const int FfmpegDecodeThreadShare = 1;

    private readonly IFfmpegToolLocator _toolLocator;
    private readonly IFfprobeService _ffprobeService;
    private readonly IFrameSamplingProcessLauncher _processLauncher;
    private readonly int _ffmpegThreadCount;

    public FrameSampler(IFfmpegToolLocator toolLocator, IFfprobeService ffprobeService, IAdaptiveResourceGovernor resourceGovernor)
        : this(toolLocator, ffprobeService, resourceGovernor, new FfmpegFrameSamplingProcessLauncher())
    {
    }

    internal FrameSampler(
        IFfmpegToolLocator toolLocator,
        IFfprobeService ffprobeService,
        IAdaptiveResourceGovernor resourceGovernor,
        IFrameSamplingProcessLauncher processLauncher)
    {
        ArgumentNullException.ThrowIfNull(toolLocator);
        ArgumentNullException.ThrowIfNull(ffprobeService);
        ArgumentNullException.ThrowIfNull(resourceGovernor);
        ArgumentNullException.ThrowIfNull(processLauncher);

        _toolLocator = toolLocator;
        _ffprobeService = ffprobeService;
        _processLauncher = processLauncher;
        _ffmpegThreadCount = ApplyCpuBudget(resourceGovernor);
    }

    // Splits IAdaptiveResourceGovernor.MaxWorkers between ffmpeg's decode
    // threads (this class's own ffmpeg invocation) and OpenCV's internal
    // thread pool (every Cv2.* call any downstream consumer of this
    // class's frames makes) so their concurrent combined usage never
    // exceeds the budget - see the class remarks. Cv2.SetNumThreads is a
    // process-wide setting; calling it repeatedly (e.g. once per
    // FrameSampler constructed) is cheap and idempotent for the same
    // governor, so no extra coordination is needed.
    private static int ApplyCpuBudget(IAdaptiveResourceGovernor resourceGovernor)
    {
        var ffmpegThreads = Math.Min(FfmpegDecodeThreadShare, resourceGovernor.MaxWorkers);
        var openCvThreads = Math.Max(1, resourceGovernor.MaxWorkers - ffmpegThreads);
        Cv2.SetNumThreads(openCvThreads);
        return ffmpegThreads;
    }

    public IAsyncEnumerable<FrameSample> SampleAsync(
        string filePath,
        FrameSamplingOptions options,
        IProgress<FrameSamplingProgress>? progress,
        CancellationToken cancellationToken = default) =>
        SampleCoreAsync(filePath, mediaInfo: null, options, progress, cancellationToken);

    public IAsyncEnumerable<FrameSample> SampleAsync(
        string filePath,
        MediaInfo mediaInfo,
        FrameSamplingOptions options,
        IProgress<FrameSamplingProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        return SampleCoreAsync(filePath, mediaInfo, options, progress, cancellationToken);
    }

    private async IAsyncEnumerable<FrameSample> SampleCoreAsync(
        string filePath,
        MediaInfo? mediaInfo,
        FrameSamplingOptions options,
        IProgress<FrameSamplingProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var validatedPath = MediaPathValidator.ValidateInputFile(filePath);
        var info = mediaInfo ?? await _ffprobeService.ProbeAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        var videoStream = info.PrimaryVideoStream
            ?? throw new FrameSamplingException($"'{validatedPath}' has no video stream to sample frames from.");

        var dimensions = FrameDimensions.ForTargetWidth(videoStream.Width, videoStream.Height, options.AnalysisWidthPixels, options.PixelFormat);
        var tools = await _toolLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
        var arguments = BuildFfmpegArguments(validatedPath, options, dimensions, _ffmpegThreadCount);

        var process = await _processLauncher.StartAsync(tools.FfmpegPath, arguments, cancellationToken).ConfigureAwait(false);

        var channel = Channel.CreateBounded<FrameSample>(new BoundedChannelOptions(options.ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        // Linked (not the raw external token) so that a consumer stopping
        // enumeration early - without ever cancelling the external token -
        // still promptly unblocks a producer that may be parked in
        // channel.Writer.WriteAsync waiting for a reader that stopped
        // reading.
        using var producerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producerTask = ProduceAsync(process, channel.Writer, dimensions, options.ChannelCapacity, progress, producerCts.Token);

        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }

            await producerTask.ConfigureAwait(false);
            await EnsureCleanExitAsync(process, validatedPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            producerCts.Cancel();
            process.Kill();
            await WaitQuietlyAsync(producerTask).ConfigureAwait(false);
            DrainRemaining(channel.Reader);
            await process.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task ProduceAsync(
        IFrameSamplingProcess process,
        ChannelWriter<FrameSample> writer,
        FrameDimensions dimensions,
        int timestampChannelCapacity,
        IProgress<FrameSamplingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var timestamps = Channel.CreateBounded<TimeSpan>(new BoundedChannelOptions(timestampChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        // The stderr pump gets its own cancellation, cancelled once the
        // frame-reading loop below stops (for any reason), so it can never
        // block forever writing a trailing log line that nothing will ever
        // read again.
        using var stderrCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stderrTask = PumpStderrTimestampsAsync(process.StandardError, timestamps.Writer, stderrCts.Token);

        Exception? failure = null;
        try
        {
            var frameIndex = 0;
            while (true)
            {
                var buffer = await RawFrameStreamReader.TryReadFrameAsync(process.StandardOutput, dimensions.ByteLength, ArrayPool<byte>.Shared, cancellationToken).ConfigureAwait(false);
                if (buffer is null)
                {
                    break;
                }

                if (!await timestamps.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)
                    || !timestamps.Reader.TryRead(out var timestamp))
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw new FrameSamplingException(
                        "ffmpeg produced a raw video frame with no matching 'showinfo' timestamp on stderr; the decode filter graph may be misconfigured.");
                }

                var sample = new FrameSample(buffer, dimensions.ByteLength, frameIndex, timestamp, dimensions.Width, dimensions.Height, dimensions.PixelFormat, ArrayPool<byte>.Shared);
                frameIndex++;

                await writer.WriteAsync(sample, cancellationToken).ConfigureAwait(false);

                progress?.Report(new FrameSamplingProgress
                {
                    FramesEmitted = frameIndex,
                    LastSourceTimestamp = timestamp,
                    Elapsed = stopwatch.Elapsed,
                });
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            stderrCts.Cancel();
        }

        try
        {
            await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected once the frame loop above has stopped consuming timestamps.
        }
        catch (Exception ex) when (failure is null)
        {
            failure = ex;
        }

        writer.Complete(failure);
    }

    private static async Task PumpStderrTimestampsAsync(TextReader stderr, ChannelWriter<TimeSpan> writer, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var line = await stderr.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (ShowInfoTimestampParser.TryParsePtsTimeSeconds(line, out var timestamp))
                {
                    await writer.WriteAsync(timestamp, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    private static async Task EnsureCleanExitAsync(IFrameSamplingProcess process, string filePath, CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new FrameSamplingException($"ffmpeg exited with code {process.ExitCode} while sampling frames from '{filePath}'.");
        }
    }

    private static async Task WaitQuietlyAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Already surfaced to the caller via the primary await path above if it mattered;
            // this is teardown-only and must never throw out of a `finally`.
        }
    }

    private static void DrainRemaining(ChannelReader<FrameSample> reader)
    {
        while (reader.TryRead(out var leftover))
        {
            leftover.Dispose();
        }
    }

    private static IReadOnlyList<string> BuildFfmpegArguments(string filePath, FrameSamplingOptions options, FrameDimensions dimensions, int threadCount)
    {
        var fps = options.SampleFramesPerSecond.ToString(CultureInfo.InvariantCulture);
        var filter = $"fps={fps},showinfo,scale={dimensions.Width}:{dimensions.Height}";

        return
        [
            "-hide_banner",
            "-nostats",
            "-loglevel", "info",
            "-threads", threadCount.ToString(CultureInfo.InvariantCulture),
            "-i", filePath,
            "-vf", filter,
            "-pix_fmt", options.PixelFormat.ToFfmpegPixelFormatName(),
            "-f", "rawvideo",
            "-an",
            "-sn",
            "pipe:1",
        ];
    }
}
