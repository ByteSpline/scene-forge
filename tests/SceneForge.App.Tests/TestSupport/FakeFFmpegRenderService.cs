using SceneForge.Media.Rendering;

namespace SceneForge.App.Tests.TestSupport;

internal sealed class FakeFFmpegRenderService : IFFmpegRenderService
{
    public RenderResult? ResultToReturn { get; set; }

    public Exception? ExceptionToThrow { get; set; }

    public RenderPlan? LastPlan { get; private set; }

    public string? LastOutputFilePath { get; private set; }

    // When set, RenderAsync awaits this before returning - lets a test hold
    // the render open long enough to call a Cancel command (see
    // FakeTransitionDetector.Gate for the same pattern).
    public TaskCompletionSource<bool>? Gate { get; set; }

    public async Task<RenderResult> RenderAsync(
        RenderPlan plan,
        string outputFilePath,
        IProgress<RenderProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        LastPlan = plan;
        LastOutputFilePath = outputFilePath;

        if (Gate is not null)
        {
            await Gate.Task;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        progress?.Report(new RenderProgress
        {
            FrameNumber = 1,
            OutTime = plan.PlannedVideoDuration,
            Elapsed = TimeSpan.FromSeconds(1),
            IsFinished = true,
        });

        return ResultToReturn ?? DefaultResult(outputFilePath, plan);
    }

    private static RenderResult DefaultResult(string outputFilePath, RenderPlan plan) => new()
    {
        OutputFilePath = outputFilePath,
        Encoder = new VideoEncoderSelection
        {
            Kind = VideoEncoderKind.SoftwareX264,
            FfmpegEncoderName = "libx264",
            IsHardwareAccelerated = false,
        },
        FellBackToSoftwareEncoder = false,
        Elapsed = TimeSpan.FromSeconds(1),
        Verification = new RenderVerificationResult
        {
            HasExpectedVideoStream = true,
            HasExactlyOneAudioStream = true,
            ExpectedDuration = plan.PlannedVideoDuration,
            ActualDuration = plan.PlannedVideoDuration,
            DurationDelta = TimeSpan.Zero,
            DurationTolerance = TimeSpan.FromMilliseconds(50),
            DurationWithinTolerance = true,
            FirstFrameDecodable = true,
            MiddleFrameDecodable = true,
            LastFrameDecodable = true,
        },
    };
}
