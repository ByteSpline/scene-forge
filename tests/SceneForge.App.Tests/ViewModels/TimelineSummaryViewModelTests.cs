using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Domain;
using SceneForge.Media.Planning;

namespace SceneForge.App.Tests.ViewModels;

public class TimelineSummaryViewModelTests
{
    [Fact]
    public async Task Constructor_NoReviewedClips_SetsErrorMessageAndDisablesContinue()
    {
        var session = new WorkflowSession { AudioMediaInfo = MediaInfoBuilder.Audio("audio.m4a", TimeSpan.FromSeconds(6)) };

        var vm = new TimelineSummaryViewModel(session, new TimelinePlanner(), new WorkflowNavigator(), new FakeProjectPersistenceCoordinator());
        await AwaitInFlightBuild(vm);

        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.IsBuilding);
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.Null(session.TimelinePlan);
    }

    [Fact]
    public async Task Constructor_ReviewedClipsCoveringTargetDuration_BuildsCompletePlan()
    {
        var session = BuildSessionWithReviewedClips();

        var vm = new TimelineSummaryViewModel(session, new TimelinePlanner(), new WorkflowNavigator(), new FakeProjectPersistenceCoordinator());
        await AwaitInFlightBuild(vm);

        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsBuilding);
        Assert.NotNull(session.TimelinePlan);
        Assert.True(vm.IsComplete);
        Assert.Null(vm.FeasibilityWarning);
        Assert.False(vm.FeasibilityWarningIsError);
        Assert.Equal(3, vm.Placements.Count);
        Assert.True(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public async Task BuildPlan_SignificantRepetitionWarning_ExposesMessageButNotAsError()
    {
        // SignificantRepetition means the target duration WAS matched - the
        // message is informational context only, so the view must not style
        // it as an error (FeasibilityWarningIsError stays false).
        var session = BuildSessionWithReviewedClips();
        var planner = new FakeTimelinePlanner
        {
            Result = PlanWithWarning(new TimelineFeasibilityWarning
            {
                Kind = TimelineFeasibilityWarningKind.SignificantRepetition,
                Message = "Significant repetition was needed to match audio length.",
                TargetDuration = TimeSpan.FromSeconds(6),
                AchievedDuration = TimeSpan.FromSeconds(6),
                Shortfall = TimeSpan.Zero,
                RequestedMaximumReuseCount = 1,
                EffectiveMaximumReuseCount = 10,
            }),
        };

        var vm = new TimelineSummaryViewModel(session, planner, new WorkflowNavigator(), new FakeProjectPersistenceCoordinator());
        await AwaitInFlightBuild(vm);

        Assert.NotNull(vm.FeasibilityWarning);
        Assert.False(vm.FeasibilityWarningIsError);
    }

    [Fact]
    public async Task BuildPlan_ShortfallWarning_IsExposedAsError()
    {
        // Shortfall means the target duration was NOT reached - a genuine
        // problem, so the view styles it red (FeasibilityWarningIsError true).
        var session = BuildSessionWithReviewedClips();
        var planner = new FakeTimelinePlanner
        {
            Result = PlanWithWarning(new TimelineFeasibilityWarning
            {
                Kind = TimelineFeasibilityWarningKind.Shortfall,
                Message = "Only 3.50s is achievable (shortfall 2.50s).",
                TargetDuration = TimeSpan.FromSeconds(6),
                AchievedDuration = TimeSpan.FromSeconds(3.5),
                Shortfall = TimeSpan.FromSeconds(2.5),
                RequestedMaximumReuseCount = 1,
                EffectiveMaximumReuseCount = 40,
            }),
        };

        var vm = new TimelineSummaryViewModel(session, planner, new WorkflowNavigator(), new FakeProjectPersistenceCoordinator());
        await AwaitInFlightBuild(vm);

        Assert.NotNull(vm.FeasibilityWarning);
        Assert.True(vm.FeasibilityWarningIsError);
    }

    private static TimelinePlan PlanWithWarning(TimelineFeasibilityWarning warning) => new()
    {
        Placements = [],
        PlannedDuration = warning.AchievedDuration,
        TargetDuration = warning.TargetDuration,
        QuantizedTargetDuration = warning.TargetDuration,
        TargetFrameCount = 0,
        AudioDurationRoundingError = TimeSpan.Zero,
        IsComplete = warning.Kind == TimelineFeasibilityWarningKind.SignificantRepetition,
        DecisionTrace = [],
        FeasibilityWarning = warning,
    };

    [Fact]
    public async Task ReshuffleCommand_IncrementsSeedAndRebuildsPlanIntoSession()
    {
        var session = BuildSessionWithReviewedClips();
        var vm = new TimelineSummaryViewModel(session, new TimelinePlanner(), new WorkflowNavigator(), new FakeProjectPersistenceCoordinator());
        await AwaitInFlightBuild(vm);
        var originalSeed = vm.Seed;

        await vm.ReshuffleCommand.ExecuteAsync(null);

        Assert.False(vm.IsBuilding);
        Assert.Equal(originalSeed + 1, vm.Seed);
        Assert.Equal(vm.Seed, session.Seed);
        Assert.NotNull(session.TimelinePlan);
    }

    [Fact]
    public async Task BuildPlan_WhileInProgress_DisablesReshuffleAndContinue()
    {
        // Deterministic assertion that the UI-thread-offload fix actually
        // gates the commands mid-flight, not merely that the final state
        // looks right afterward - uses FakeTimelinePlanner's Gate (the same
        // pattern AnalysisProgressViewModelTests already uses for
        // ITransitionDetector) rather than a wall-clock timing assumption,
        // so this cannot flake regardless of machine speed.
        var session = BuildSessionWithReviewedClips();
        var gate = new TaskCompletionSource<bool>();
        var planner = new FakeTimelinePlanner { Gate = gate };

        var vm = new TimelineSummaryViewModel(session, planner, new WorkflowNavigator(), new FakeProjectPersistenceCoordinator());

        Assert.True(vm.IsBuilding);
        Assert.False(vm.ReshuffleCommand.CanExecute(null));
        Assert.False(vm.ContinueCommand.CanExecute(null));

        gate.SetResult(true);
        await AwaitInFlightBuild(vm);

        Assert.False(vm.IsBuilding);
        Assert.True(vm.ReshuffleCommand.CanExecute(null));
    }

    [Fact]
    public async Task BuildPlan_PassesALiveCancellationToken_NotCancellationTokenNone()
    {
        // TimelinePlanner.Plan checks CancellationToken once per placement
        // (CLAUDE.md rule 5) - that guarantee is worthless if the caller
        // always passes CancellationToken.None. Confirms BuildPlan threads
        // a real, cancelable token from its own CancellationTokenSource
        // through Task.Run, not the default.
        var session = BuildSessionWithReviewedClips();
        var planner = new FakeTimelinePlanner();

        var vm = new TimelineSummaryViewModel(session, planner, new WorkflowNavigator(), new FakeProjectPersistenceCoordinator());
        await AwaitInFlightBuild(vm);

        Assert.NotNull(planner.CapturedCancellationToken);
        Assert.NotEqual(CancellationToken.None, planner.CapturedCancellationToken!.Value);
        Assert.True(planner.CapturedCancellationToken.Value.CanBeCanceled);
    }

    [Fact]
    public async Task BuildPlan_UnderlyingPlanIsCanceled_RecoversWithoutStuckBuildingState()
    {
        // Verifies BuildPlan's catch(OperationCanceledException) path
        // actually clears IsBuilding and leaves the ViewModel usable again
        // (CLAUDE.md rule 5: cooperative shutdown must not leave the UI
        // permanently disabled) rather than letting the exception escape
        // unhandled or leaving IsBuilding stuck true.
        var session = BuildSessionWithReviewedClips();
        var planner = new FakeTimelinePlanner { ThrowInstead = new OperationCanceledException() };

        var vm = new TimelineSummaryViewModel(session, planner, new WorkflowNavigator(), new FakeProjectPersistenceCoordinator());
        await AwaitInFlightBuild(vm);

        Assert.False(vm.IsBuilding);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Null(session.TimelinePlan);
        Assert.True(vm.ReshuffleCommand.CanExecute(null));
    }

    [Fact]
    public async Task ContinueCommand_Execute_NavigatesToExportSettings()
    {
        var session = BuildSessionWithReviewedClips();
        var navigator = new WorkflowNavigator();
        var persistence = new FakeProjectPersistenceCoordinator();
        var vm = new TimelineSummaryViewModel(session, new TimelinePlanner(), navigator, persistence);
        await AwaitInFlightBuild(vm);

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(WorkflowStep.ExportSettings, navigator.CurrentStep);
        Assert.Contains(ProjectStage.TimelinePlanned, persistence.CheckpointedStages);
    }

    private static async Task AwaitInFlightBuild(TimelineSummaryViewModel vm)
    {
        if (vm.BuildPlanCommand.ExecutionTask is { IsCompleted: false } inFlight)
        {
            await inFlight;
        }
    }

    private static WorkflowSession BuildSessionWithReviewedClips()
    {
        var clips = new List<SceneForge.Media.Extraction.CleanClip>
        {
            CleanClipBuilder.Build(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), accepted: true, sourceSceneIndex: 0),
            CleanClipBuilder.Build(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12), accepted: true, sourceSceneIndex: 1),
            CleanClipBuilder.Build(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(22), accepted: true, sourceSceneIndex: 2),
        };

        return new WorkflowSession
        {
            ReviewedClips = clips,
            AudioMediaInfo = MediaInfoBuilder.Audio("audio.m4a", TimeSpan.FromSeconds(6)),
            OutputFrameRate = new RationalFrameRate(30, 1),
            Seed = 1,
        };
    }
}
