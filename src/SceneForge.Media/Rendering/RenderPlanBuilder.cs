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

        var segments = new List<RenderSegment>(plan.Placements.Count);
        foreach (var placement in plan.Placements)
        {
            var segmentEnd = placement.SourceRange.Start + placement.UsedDuration;
            if (videoStream.Duration is { } sourceDuration && segmentEnd - sourceDuration > SourceDurationSlack)
            {
                throw new RenderPlanException(
                    $"Placement {placement.Position} requires source footage up to {segmentEnd}, but the probed source video duration is only {sourceDuration}.");
            }

            segments.Add(new RenderSegment
            {
                Position = placement.Position,
                SourceStart = placement.SourceRange.Start,
                SourceDuration = placement.UsedDuration,
                IsTrimmed = placement.IsTrimmed,
            });
        }

        segments.Sort((a, b) => a.Position.CompareTo(b.Position));

        return new RenderPlan
        {
            SourceFilePath = sourcePath,
            Segments = segments,
            OutputSpec = request.OutputSpec,
            Audio = audio with { FilePath = audioPath },
            SourceRotationDegrees = videoStream.RotationDegrees,
            PlannedVideoDuration = plan.PlannedDuration,
        };
    }
}
