using SceneForge.Media.Planning;

namespace SceneForge.App.Tests.TestSupport;

// ITimelinePlanner.Plan is synchronous (see its own remarks - a deliberate
// CPU-only-pipeline-stage design), so this fake blocks the calling thread
// (expected to be a Task.Run thread pool thread, never the test's own
// thread directly - see TimelineSummaryViewModelTests) rather than
// awaiting, mirroring FakeTransitionDetector's Gate pattern for the
// synchronous case: lets a test deterministically hold TimelineSummaryViewModel.BuildPlan
// open long enough to observe IsBuilding/CanExecute mid-flight without
// relying on wall-clock timing.
internal sealed class FakeTimelinePlanner : ITimelinePlanner
{
    public TimelinePlan Result { get; set; } = new()
    {
        Placements = [],
        PlannedDuration = TimeSpan.Zero,
        TargetDuration = TimeSpan.Zero,
        QuantizedTargetDuration = TimeSpan.Zero,
        TargetFrameCount = 0,
        AudioDurationRoundingError = TimeSpan.Zero,
        IsComplete = true,
        DecisionTrace = [],
        FeasibilityWarning = null,
    };

    public TaskCompletionSource<bool>? Gate { get; set; }

    // If set, Plan throws this instead of returning Result - lets a test
    // verify the caller's catch/recovery path without needing a real,
    // separately-triggered CancellationToken.
    public Exception? ThrowInstead { get; set; }

    // The CancellationToken this fake was actually invoked with, captured
    // for a test to assert against - verifies a caller passes a real,
    // live token through (not CancellationToken.None) without needing to
    // actually trigger cancellation end-to-end.
    public CancellationToken? CapturedCancellationToken { get; private set; }

    public TimelinePlan Plan(TimelinePlanRequest request, CancellationToken cancellationToken = default)
    {
        CapturedCancellationToken = cancellationToken;

        if (Gate is not null)
        {
            Gate.Task.Wait(cancellationToken);
        }

        if (ThrowInstead is not null)
        {
            throw ThrowInstead;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Result;
    }
}
