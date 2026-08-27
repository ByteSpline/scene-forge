using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.App.Navigation;
using SceneForge.App.Persistence;
using SceneForge.App.Session;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;

namespace SceneForge.App.ViewModels;

// Step 5: builds a TimelinePlan from Scene Review's reviewed clip list and
// displays it. "Reshuffle" re-plans with a new seed for a different (still
// deterministic once chosen - CLAUDE.md rule 10) ordering.
//
// TimelinePlanner.Plan is itself synchronous, pure CPU work (see
// ITimelinePlanner) - Phase 8 built it that way because it was always fast
// (MaximumReuseCount was a small, never-relaxed hard cap, so an infeasible
// plan gave up almost immediately). Phase 16 changed that: MaximumReuseCount
// now relaxes automatically when footage is insufficient, so Plan can
// legitimately run for hundreds of milliseconds to several seconds against
// a large clip pool and/or long target duration - confirmed by direct
// measurement in the Phase 16 release review (docs/PHASE_REPORT.md): ~1s
// for 500 clips against a 4-hour target, and multiple seconds in more
// extreme cases. Calling it synchronously on the UI thread, as this
// ViewModel originally did, would freeze the window for that entire span
// with no way to cancel - a CLAUDE.md rule 5 violation once Plan can
// actually take this long. BuildPlan now offloads the call via Task.Run and
// threads a real CancellationToken through, the same
// IsRunning/CanExecute/CancellationTokenSource shape
// AnalysisProgressViewModel already established for exactly this concern,
// adapted for a CPU-bound Task.Run instead of already-async I/O.
public sealed partial class TimelineSummaryViewModel : ObservableObject, IDisposable
{
    private readonly WorkflowSession _session;
    private readonly ITimelinePlanner _timelinePlanner;
    private readonly IWorkflowNavigator _navigator;
    private readonly IProjectPersistenceCoordinator _persistence;

    private CancellationTokenSource? _cancellationTokenSource;

    public ObservableCollection<TimelinePlacementRowViewModel> Placements { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReshuffleCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildPlanCommand))]
    private bool isBuilding;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private TimeSpan plannedDuration;

    [ObservableProperty]
    private TimeSpan targetDuration;

    [ObservableProperty]
    private bool isComplete;

    [ObservableProperty]
    private string? feasibilityWarning;

    [ObservableProperty]
    private int seed;

    public TimelineSummaryViewModel(
        WorkflowSession session,
        ITimelinePlanner timelinePlanner,
        IWorkflowNavigator navigator,
        IProjectPersistenceCoordinator persistence)
    {
        _session = session;
        _timelinePlanner = timelinePlanner;
        _navigator = navigator;
        _persistence = persistence;

        seed = session.Seed;
        _ = BuildPlanCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanReshuffle))]
    private async Task Reshuffle()
    {
        Seed++;
        await BuildPlanCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    private bool CanReshuffle() => !IsBuilding;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task Continue()
    {
        await _persistence.CheckpointAsync(_session, ProjectStage.TimelinePlanned);
        _navigator.NavigateTo(WorkflowStep.ExportSettings);
    }

    private bool CanContinue() => !IsBuilding && _session.TimelinePlan is not null && ErrorMessage is null;

    private bool CanBuildPlan() => !IsBuilding;

    [RelayCommand(CanExecute = nameof(CanBuildPlan))]
    private async Task BuildPlan()
    {
        ErrorMessage = null;
        IsBuilding = true;

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;

        try
        {
            var clips = _session.ReviewedClips;
            var audioInfo = _session.AudioMediaInfo;
            if (clips is null || clips.Count == 0 || audioInfo is null)
            {
                ErrorMessage = "No clips were included in Scene Review. Go back and include at least one clip.";
                _session.TimelinePlan = null;
                return;
            }

            var request = new TimelinePlanRequest
            {
                AvailableClips = clips,
                TargetAudioDuration = audioInfo.Duration,
                OutputTimeBase = _session.OutputFrameRate,
                Seed = Seed,
            };

            var plan = await Task.Run(() => _timelinePlanner.Plan(request, cancellationToken), cancellationToken).ConfigureAwait(true);

            _session.TimelinePlan = plan;
            _session.Seed = Seed;

            Placements.Clear();
            foreach (var placement in plan.Placements)
            {
                Placements.Add(new TimelinePlacementRowViewModel(placement, clips[placement.ClipIndex]));
            }

            PlannedDuration = plan.PlannedDuration;
            TargetDuration = plan.QuantizedTargetDuration;
            IsComplete = plan.IsComplete;
            FeasibilityWarning = plan.FeasibilityWarning?.Message;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Timeline planning was canceled.";
            _session.TimelinePlan = null;
        }
        catch (ArgumentException ex)
        {
            _session.TimelinePlan = null;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBuilding = false;
            ContinueCommand.NotifyCanExecuteChanged();
        }
    }

    public void Dispose() => _cancellationTokenSource?.Dispose();
}

// One row of the resulting plan: which reviewed clip landed at which
// position, and whether it was trimmed to make the audio track's duration
// land exactly (see TimelinePlacement.IsTrimmed).
public sealed record TimelinePlacementRowViewModel(TimelinePlacement Placement, CleanClip Clip)
{
    public int DisplayPosition => Placement.Position + 1;

    public TimeSpan Duration => Placement.UsedDuration;

    public bool IsTrimmed => Placement.IsTrimmed;

    public int UsageOrdinal => Placement.UsageOrdinal;
}
