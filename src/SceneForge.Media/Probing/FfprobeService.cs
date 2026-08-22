using System.Globalization;
using System.Text.Json;
using SceneForge.Media.Domain;
using SceneForge.Media.Probing.Json;
using SceneForge.Media.Processes;
using SceneForge.Media.Tooling;
using SceneForge.Media.Validation;

namespace SceneForge.Media.Probing;

public sealed class FfprobeService : IFfprobeService
{
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(30);

    // ffprobe reports r_frame_rate/avg_frame_rate mismatches down to fractions
    // of a frame per second even for constant frame rate content because of
    // rounding; this tolerance keeps that noise from being reported as VFR.
    private const double FrameRateEpsilon = 0.01;

    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly TimeSpan _probeTimeout;

    public FfprobeService(IProcessRunner processRunner, IFfmpegToolLocator toolLocator, TimeSpan? probeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(toolLocator);

        _processRunner = processRunner;
        _toolLocator = toolLocator;
        _probeTimeout = probeTimeout ?? DefaultProbeTimeout;
    }

    public async Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        var validatedPath = MediaPathValidator.ValidateInputFile(filePath);
        var tools = await _toolLocator.LocateAsync(cancellationToken).ConfigureAwait(false);

        ProcessExecutionResult result;
        try
        {
            result = await _processRunner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = tools.FfprobePath,
                    Arguments = ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", validatedPath],
                    Timeout = _probeTimeout,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ProcessTimeoutException ex)
        {
            throw new FfprobeExecutionException($"ffprobe did not finish probing '{validatedPath}' within the configured timeout.", ex);
        }

        if (result.ExitCode != 0)
        {
            throw new FfprobeExecutionException(result.ExitCode, result.StandardError);
        }

        FfprobeOutputDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FfprobeOutputDto>(result.StandardOutput);
        }
        catch (JsonException ex)
        {
            throw new FfprobeExecutionException($"ffprobe produced output for '{validatedPath}' that could not be parsed as JSON.", ex);
        }

        if (dto is null)
        {
            throw new FfprobeExecutionException($"ffprobe produced empty output for '{validatedPath}'.", new InvalidOperationException("Deserialized ffprobe output was null."));
        }

        return MapToMediaInfo(validatedPath, dto);
    }

    private static MediaInfo MapToMediaInfo(string filePath, FfprobeOutputDto dto)
    {
        var videoStreams = new List<VideoStreamInfo>();
        var audioStreams = new List<AudioStreamInfo>();

        foreach (var stream in dto.Streams ?? [])
        {
            switch (stream.CodecType)
            {
                case "video":
                    videoStreams.Add(MapVideoStream(stream));
                    break;
                case "audio":
                    audioStreams.Add(MapAudioStream(stream));
                    break;
            }
        }

        if (videoStreams.Count == 0 && audioStreams.Count == 0)
        {
            throw new MediaValidationException(MediaValidationFailureReason.NoMediaStreams, $"'{filePath}' has no video or audio streams.");
        }

        var duration = ParseSecondsOrNull(dto.Format?.Duration) ?? MaxStreamDurationOrNull(videoStreams, audioStreams);
        if (duration is null)
        {
            throw new MediaValidationException(MediaValidationFailureReason.UnknownDuration, $"'{filePath}' does not report a usable duration.");
        }

        return new MediaInfo
        {
            FilePath = filePath,
            FormatName = dto.Format?.FormatName ?? "unknown",
            Duration = duration.Value,
            BitRateBitsPerSecond = ParseLongOrNull(dto.Format?.BitRate),
            VideoStreams = videoStreams.AsReadOnly(),
            AudioStreams = audioStreams.AsReadOnly(),
        };
    }

    private static TimeSpan? MaxStreamDurationOrNull(List<VideoStreamInfo> videoStreams, List<AudioStreamInfo> audioStreams)
    {
        var candidates = videoStreams.Select(v => v.Duration)
            .Concat(audioStreams.Select(a => a.Duration))
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        return candidates.Count > 0 ? candidates.Max() : null;
    }

    private static VideoStreamInfo MapVideoStream(FfprobeStreamDto stream)
    {
        var realBaseFrameRate = RationalFrameRate.Parse(stream.RFrameRate);
        var averageFrameRate = RationalFrameRate.Parse(stream.AvgFrameRate);

        return new VideoStreamInfo
        {
            Index = stream.Index,
            CodecName = stream.CodecName ?? "unknown",
            Width = RequireField(stream.Width, stream.Index, "width"),
            Height = RequireField(stream.Height, stream.Index, "height"),
            AverageFrameRate = averageFrameRate,
            RealBaseFrameRate = realBaseFrameRate,
            IsVariableFrameRate = IsVariableFrameRate(realBaseFrameRate, averageFrameRate),
            RotationDegrees = ResolveRotationDegrees(stream),
            PixelFormat = stream.PixelFormat,
            BitRateBitsPerSecond = ParseLongOrNull(stream.BitRate),
            Duration = ResolveStreamDuration(stream),
        };
    }

    private static AudioStreamInfo MapAudioStream(FfprobeStreamDto stream) => new()
    {
        Index = stream.Index,
        CodecName = stream.CodecName ?? "unknown",
        SampleRateHz = RequireField(ParseIntOrNull(stream.SampleRate), stream.Index, "sample_rate"),
        Channels = RequireField(stream.Channels, stream.Index, "channels"),
        ChannelLayout = stream.ChannelLayout,
        BitRateBitsPerSecond = ParseLongOrNull(stream.BitRate),
        Duration = ResolveStreamDuration(stream),
    };

    // width/height/sample_rate/channels are load-bearing for every downstream
    // consumer (frame geometry, PCM math); defaulting a missing one to 0
    // would silently manufacture a plausible-looking but meaningless
    // MediaInfo instead of surfacing the malformed ffprobe output.
    private static int RequireField(int? value, int streamIndex, string fieldName) =>
        value ?? throw new FfprobeExecutionException($"Stream {streamIndex} is missing a usable '{fieldName}' field.");

    private static bool IsVariableFrameRate(RationalFrameRate realBaseFrameRate, RationalFrameRate averageFrameRate)
    {
        var real = realBaseFrameRate.ToDouble();
        var average = averageFrameRate.ToDouble();
        if (real is null || average is null)
        {
            return false;
        }

        return Math.Abs(real.Value - average.Value) > FrameRateEpsilon;
    }

    // Reports ffprobe's own raw rotation value normalized into [0, 360),
    // preferring the modern "Display Matrix" side data over the legacy
    // "rotate" tag when both are present. This has only been verified
    // against crafted fixtures, not a real device-recorded rotated file
    // (this ffmpeg build's mp4 muxer would not reproduce rotation metadata
    // for a locally generated one - see docs/PHASE_04_REPORT.md) - callers
    // should treat 0 as "no rotation reported" and other values as
    // ffprobe's own convention, not an independently verified physical
    // rotation direction.
    private static int ResolveRotationDegrees(FfprobeStreamDto stream)
    {
        var displayMatrixRotation = stream.SideDataList?
            .FirstOrDefault(sideData => sideData.SideDataType == "Display Matrix" && sideData.Rotation.HasValue)?
            .Rotation;

        if (displayMatrixRotation.HasValue)
        {
            return NormalizeRotationDegrees(displayMatrixRotation.Value);
        }

        if (stream.Tags is not null
            && stream.Tags.TryGetValue("rotate", out var rotateTag)
            && double.TryParse(rotateTag, NumberStyles.Float, CultureInfo.InvariantCulture, out var rotateDegrees))
        {
            return NormalizeRotationDegrees(rotateDegrees);
        }

        return 0;
    }

    private static int NormalizeRotationDegrees(double rawDegrees)
    {
        var normalized = rawDegrees % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return (int)Math.Round(normalized / 90.0) * 90 % 360;
    }

    private static TimeSpan? ResolveStreamDuration(FfprobeStreamDto stream)
    {
        var direct = ParseSecondsOrNull(stream.Duration);
        if (direct.HasValue)
        {
            return direct;
        }

        if (stream.DurationTimestamps is { } timestamps
            && TryParseRational(stream.TimeBase, out var numerator, out var denominator)
            && denominator != 0)
        {
            return TimeSpan.FromSeconds(timestamps * ((double)numerator / denominator));
        }

        return null;
    }

    private static bool TryParseRational(string? value, out long numerator, out long denominator)
    {
        numerator = 0;
        denominator = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('/', 2);
        return parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out numerator)
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out denominator);
    }

    private static TimeSpan? ParseSecondsOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static long? ParseLongOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static int? ParseIntOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    }
}
