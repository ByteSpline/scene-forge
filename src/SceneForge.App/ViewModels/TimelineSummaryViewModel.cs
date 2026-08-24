using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;

namespace SceneForge.App.ViewModels;

// Step 5: builds a TimelinePlan (pure, synchronous - see ITimelinePlanner)
// from Scene Review's reviewed clip list and displays it. "Reshuffle"
// re-plans with a new seed for a different (still deterministic once
// chosen - CLAUDE.md rule 10) ordering, cheaply, since TimelinePlanner.Plan
// does no I/O.
public sealed partial class TimelineSummaryViewModel : ObservableObject
{
    private readonly WorkflowSession _session;
    private readonly ITimelinePlanner _timelinePlanner;
    private readonly IWorkflowNavigator _navigator;

    public ObservableCollection<TimelinePlacementRowViewModel> Placements { get; } = [];

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

    public TimelineSummaryViewModel(WorkflowSession session, ITimelinePlanner timelinePlanner, IWorkflowNavigator navigator)
    {
        _session = session;
        _timelinePlanner = timelinePlanner;
        _navigator = navigator;

        seed = session.Seed;
        BuildPlan();
    }

    [RelayCommand]
    private void Reshuffle()
    {
        Seed++;
        BuildPlan();
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue() => _navigator.NavigateTo(WorkflowStep.ExportSettings);

    private bool CanContinue() => _session.TimelinePlan is not null && ErrorMessage is null;

    private void BuildPlan()
    {
        ErrorMessage = null;

        var clips = _session.ReviewedClips;
        var audioInfo = _session.AudioMediaInfo;
        if (clips is null || clips.Count == 0 || audioInfo is null)
        {
            ErrorMessage = "No clips were included in Scene Review. Go back and include at least one clip.";
            _session.TimelinePlan = null;
            ContinueCommand.NotifyCanExecuteChanged();
            return;
        }

        var request = new TimelinePlanRequest
        {
            AvailableClips = clips,
            TargetAudioDuration = audioInfo.Duration,
            OutputTimeBase = _session.OutputFrameRate,
            Seed = Seed,
        };

        try
        {
            var plan = _timelinePlanner.Plan(request);
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
        catch (ArgumentException ex)
        {
            _session.TimelinePlan = null;
            ErrorMessage = ex.Message;
        }

        ContinueCommand.NotifyCanExecuteChanged();
    }
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
