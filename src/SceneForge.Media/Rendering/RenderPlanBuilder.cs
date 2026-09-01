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
        var orderedPlacements = plan.Placements.OrderBy(p => p.Position).ToList();
        var segments = new List<RenderSegment>(orderedPlacements.Count);

        // ffmpeg's trim filter keeps every source frame whose presentation
        // time falls within [start, start+duration) - for a duration that
        // is not an exact multiple of the frame period, the last frame
        // that starts inside that window is kept in full even though the
        // window's nominal end falls partway through that frame's own
        // display period, so an unquantized duration handed straight to
        // ffmpeg gets whatever frame count happens to overlap the window,
        // not a value SceneForge chose (verified directly against real
        // ffmpeg: this is deterministic filter behavior, not an
        // encoder/version-specific quirk) - every segment duration below
        // is therefore quantized to a whole number of frames before it is
        // ever used as a trim=duration= argument (see
        // docs/OPTIMIZATION_REPORT.md's investigation).
        //
        // Quantizing each placement's OWN duration independently
        // (MidpointRounding.AwayFromZero against that placement's
        // UsedDuration alone, the original fix) bounds any ONE segment's
        // error to half a frame, but says nothing about the SUM: repeat
        // the same slightly-long clip through DistinctDedup's own
        // high-repetition path and a fixed per-clip rounding bias
        // multiplies by the repeat count instead of cancelling out, and
        // even without repetition a many-hundred-placement Batched plan
        // sees the summed error grow with placement count via ordinary
        // random-walk accumulation - either way, the sum of independently-
        // quantized segments can end up several frames away from
        // TimelinePlan.PlannedDuration (what RenderAudioTrackSpec.TrimDuration
        // is normally set to - see that type's own remarks), even though
        // no single segment is individually wrong, and the gap grows with
        // scale rather than staying fixed - exactly why this surfaced
        // differently (and independently) on SinglePass, DistinctDedup,
        // and Batched as each was built, instead of being caught once.
        //
        // The fix is the standard "largest remainder"/Bresenham
        // apportionment technique: track the cumulative IDEAL (continuous,
        // un-quantized) duration and the cumulative frame count already
        // committed to segments, in placement-Position order, and assign
        // each placement only the DELTA of frames needed to bring the
        // running total's rounded frame count back in line. This bounds
        // the error at every prefix - not just the grand total - to under
        // one frame, by construction, regardless of placement count or
        // repetition pattern, so PlannedVideoDuration (the final
        // cumulative frame count converted back to a duration, below)
        // always agrees with TimelinePlan.PlannedDuration to within a
        // single frame at the render's own frame rate, no matter how many
        // segments or how much repetition the plan has.
        //
        // A byte-identical repeated window can still land on two (rarely
        // three) different quantized durations depending on where it
        // falls in the running phase, rather than always the exact same
        // one - CountDistinctSegments/RenderDistinctDedupStageAAsync
        // already key pre-render pieces by (SourceStart, SourceDuration),
        // so this is handled automatically and just turns into at most
        // ~2-3x as many pre-render pieces for a given distinct-window
        // count (still far below MaxDistinctDedupPieces / the total
        // segment count for any realistic high-repetition plan), never an
        // unbounded per-occurrence explosion.
        var cumulativeIdealDuration = TimeSpan.Zero;
        var cumulativeFrameCount = 0L;

        foreach (var placement in orderedPlacements)
        {
            cumulativeIdealDuration += placement.UsedDuration;
            var cumulativeFrameCountAfter = frameRate.ToFrameCount(cumulativeIdealDuration);
            var frameCount = cumulativeFrameCountAfter - cumulativeFrameCount;
            if (frameCount <= 0)
            {
                throw new RenderPlanException(
                    $"Placement {placement.Position}'s duration ({placement.UsedDuration}) rounds to {frameCount} frames at the output frame rate {frameRate} - too short to render.");
            }

            cumulativeFrameCount = cumulativeFrameCountAfter;
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
        }

        // The exact duration of the cumulative frame count committed above
        // - not a running TimeSpan sum of the individually-rounded segment
        // durations, which would reintroduce its own (much smaller, but
        // nonzero) per-addition tick-rounding error across many segments.
        var plannedVideoDuration = frameRate.FromFrameCount(cumulativeFrameCount);

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
