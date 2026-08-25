using SceneForge.Media.Validation;

namespace SceneForge.Media.Rendering;

// Turns one TimelinePlan into a concrete RenderPlan: resolves the source
// file path once, carries every placement's SourceRange/UsedDuration
// forward untouched as RenderSegment trims (see RenderSegment - always the
// ORIGINAL file's timestamps, never an analysis proxy's), and validates
// every segment actually fits inside the probed source duration before
// FFmpegRenderService ever spawns a process. Pure and synchronous - the
// same shape TimelinePlanner already established (docs/PHASE_08_REPORT.md).
public sealed class RenderPlanBuilder : IRenderPlanBuilder
{
    // ffprobe's reported container duration is occasionally a few
    // milliseconds short of what is actually seekable/decodable (container
    // overhead, rounding) - this absorbs that noise without silently
    // accepting a placement that genuinely reaches past the source's real
    // content.
    private static readonly TimeSpan SourceDurationSlack = TimeSpan.FromMilliseconds(500);

    public RenderPlan Build(RenderPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = request.TimelinePlan ?? throw new RenderPlanException("RenderPlanRequest.TimelinePlan is required.");
        if (plan.Placements.Count == 0)
        {
            throw new RenderPlanException("Cannot build a RenderPlan from a TimelinePlan with no placements.");
        }

        var mediaInfo = request.SourceMediaInfo ?? throw new RenderPlanException("RenderPlanRequest.SourceMediaInfo is required.");
        var videoStream = mediaInfo.PrimaryVideoStream
            ?? throw new RenderPlanException($"'{request.SourceFilePath}' has no video stream to render from.");

        var sourcePath = MediaPathValidator.ValidateInputFile(request.SourceFilePath);
        var audio = request.Audio ?? throw new RenderPlanException("RenderPlanRequest.Audio is required.");
        var audioPath = MediaPathValidator.ValidateInputFile(audio.FilePath);

        if (audio.TrimStart < TimeSpan.Zero)
        {
            throw new RenderPlanException("RenderAudioTrackSpec.TrimStart must not be negative.");
        }

        if (audio.TrimDuration <= TimeSpan.Zero)
        {
            throw new RenderPlanException("RenderAudioTrackSpec.TrimDuration must be positive.");
        }

        if (request.OutputSpec is null)
        {
            throw new RenderPlanException("RenderPlanRequest.OutputSpec is required.");
        }

        if (!request.OutputSpec.FrameRate.IsDefined)
        {
            throw new RenderPlanException("RenderOutputSpec.FrameRate must be a defined rate.");
        }

        var frameRate = request.OutputSpec.FrameRate;
        var segments = new List<RenderSegment>(plan.Placements.Count);
        var plannedVideoDuration = TimeSpan.Zero;
        foreach (var placement in plan.Placements)
        {
            // ffmpeg's trim filter keeps every source frame whose
            // presentation time falls within [start, start+duration) - for
            // a duration that is not an exact multiple of the frame
            // period, the last frame that starts inside that window is
            // kept in full even though the window's nominal end falls
            // partway through that frame's own display period, so an
            // unquantized duration handed straight to ffmpeg gets whatever
            // frame count happens to overlap the window, not a value
            // SceneForge chose (verified directly against real ffmpeg:
            // this is deterministic filter behavior, not an
            // encoder/version-specific quirk). Rounding to the NEAREST
            // whole frame here - which may land above or below the
            // original duration, unlike trim's own always-keep-the-
            // overlapping-frame behavior - produces a duration that IS an
            // exact multiple of the frame period, and a trim window whose
            // width is an exact multiple of the frame period always
            // produces exactly that many frames regardless of phase
            // (verified directly against real ffmpeg across multiple
            // segment counts) - so both this segment's actual rendered
            // frame count and PlannedVideoDuration (the running sum below)
            // are guaranteed to agree with what ffmpeg will actually
            // produce, instead of silently drifting further apart with
            // every additional segment (see docs/OPTIMIZATION_REPORT.md's
            // investigation - this was a real, measured,
            // per-segment-accumulating bug, not an unavoidable ffmpeg
            // quirk that tolerance alone could paper over). Same
            // MidpointRounding.AwayFromZero convention TimelinePlanner
            // already uses for its own target-duration quantization.
            var frameCount = frameRate.ToFrameCount(placement.UsedDuration);
            if (frameCount <= 0)
            {
                throw new RenderPlanException(
                    $"Placement {placement.Position}'s duration ({placement.UsedDuration}) rounds to {frameCount} frames at the output frame rate {frameRate} - too short to render.");
            }

            var quantizedDuration = frameRate.FromFrameCount(frameCount);

            var segmentEnd = placement.SourceRange.Start + quantizedDuration;
            if (videoStream.Duration is { } sourceDuration && segmentEnd - sourceDuration > SourceDurationSlack)
            {
                throw new RenderPlanException(
                    $"Placement {placement.Position} requires source footage up to {segmentEnd}, but the probed source video duration is only {sourceDuration}.");
            }

            segments.Add(new RenderSegment
            {
                Position = placement.Position,
                SourceStart = placement.SourceRange.Start,
                SourceDuration = quantizedDuration,
                IsTrimmed = placement.IsTrimmed,
            });
            plannedVideoDuration += quantizedDuration;
        }

        segments.Sort((a, b) => a.Position.CompareTo(b.Position));

        return new RenderPlan
        {
            SourceFilePath = sourcePath,
            Segments = segments,
            OutputSpec = request.OutputSpec,
            Audio = audio with { FilePath = audioPath },
            SourceRotationDegrees = videoStream.RotationDegrees,
            PlannedVideoDuration = plannedVideoDuration,
        };
    }
}
