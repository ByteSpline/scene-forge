using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.App.Navigation;
using SceneForge.App.Persistence;
using SceneForge.App.Services;
using SceneForge.App.Session;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Rendering;

namespace SceneForge.App.ViewModels;

// Step 6: output geometry/fit and the destination path (CLAUDE.md rule 12 -
// always a user-chosen new path, never the source). Frame rate is
// deliberately not editable here - it was fixed in Analysis Settings and
// must stay identical between TimelinePlanRequest.OutputTimeBase and
// RenderOutputSpec.FrameRate (see Session.WorkflowSession's remarks), so it
// is shown read-only instead. Building the RenderPlan (IRenderPlanBuilder,
// pure and synchronous) happens here, before navigating to Render Progress,
// so a caller-input mismatch (e.g. a stale TimelinePlan) surfaces as an
// inline error on this screen rather than after a render has already
// started.
public sealed partial class ExportSettingsViewModel : ObservableObject
{
    private readonly WorkflowSession _session;
    private readonly IRenderPlanBuilder _renderPlanBuilder;
    private readonly IDialogService _dialogService;
    private readonly IWorkflowNavigator _navigator;
    private readonly IProjectPersistenceCoordinator _persistence;

    public IReadOnlyList<ResolutionOption> AvailableResolutions { get; } = ResolutionOption.Defaults;

    public IReadOnlyList<AspectFitMode> AvailableFitModes { get; } = Enum.GetValues<AspectFitMode>();

    [ObservableProperty]
    private ResolutionOption selectedResolution;

    [ObservableProperty]
    private AspectFitMode selectedFitMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string? outputVideoPath;

    [ObservableProperty]
    private string? errorMessage;

    public string FrameRateSummary { get; }

    public ExportSettingsViewModel(
        WorkflowSession session,
        IRenderPlanBuilder renderPlanBuilder,
        IDialogService dialogService,
        IWorkflowNavigator navigator,
        IProjectPersistenceCoordinator persistence)
    {
        _session = session;
        _renderPlanBuilder = renderPlanBuilder;
        _dialogService = dialogService;
        _navigator = navigator;
        _persistence = persistence;

        selectedResolution = AvailableResolutions.FirstOrDefault(r => r.Width == session.OutputWidth && r.Height == session.OutputHeight)
            ?? AvailableResolutions[0];
        selectedFitMode = session.FitMode;
        outputVideoPath = session.OutputVideoPath;

        var fps = session.OutputFrameRate.ToDouble();
        FrameRateSummary = fps is null
            ? "Output frame rate: undefined"
            : $"Output frame rate: {fps:0.###} fps (fixed in Analysis Settings)";
    }

    [RelayCommand]
    private void BrowseOutputPath()
    {
        var suggestedName = Path.GetFileNameWithoutExtension(_session.VideoFilePath ?? "output") + "_sceneforge.mp4";
        var path = _dialogService.ShowSaveVideoFileDialog(suggestedName);
        if (path is not null)
        {
            OutputVideoPath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task Continue()
    {
        ErrorMessage = null;

        var timelinePlan = _session.TimelinePlan;
        var videoPath = _session.VideoFilePath;
        var videoMediaInfo = _session.VideoMediaInfo;
        var audioPath = _session.AudioFilePath;

        if (timelinePlan is null || videoPath is null || videoMediaInfo is null || audioPath is null || OutputVideoPath is null)
        {
            ErrorMessage = "Earlier workflow steps must be completed before export settings can be applied.";
            return;
        }

        try
        {
            var request = new RenderPlanRequest
            {
                TimelinePlan = timelinePlan,
                SourceFilePath = videoPath,
                SourceMediaInfo = videoMediaInfo,
                OutputSpec = new RenderOutputSpec
                {
                    Width = SelectedResolution.Width,
                    Height = SelectedResolution.Height,
                    FrameRate = _session.OutputFrameRate,
                    FitMode = SelectedFitMode,
                },
                Audio = new RenderAudioTrackSpec
                {
                    FilePath = audioPath,
                    TrimDuration = timelinePlan.PlannedDuration,
                },
            };

            _session.RenderPlan = _renderPlanBuilder.Build(request);
            _session.OutputVideoPath = OutputVideoPath;
            _session.FitMode = SelectedFitMode;
            _session.OutputWidth = SelectedResolution.Width;
            _session.OutputHeight = SelectedResolution.Height;

            await _persistence.CheckpointAsync(_session, ProjectStage.RenderConfigured);

            _navigator.NavigateTo(WorkflowStep.RenderProgress);
        }
        catch (RenderPlanException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private bool CanContinue() => !string.IsNullOrWhiteSpace(OutputVideoPath) && _session.TimelinePlan is not null;
}

// One selectable output resolution. ToString() returns Label so a plain
// ComboBox (no DisplayMemberPath) shows the friendly name directly.
public sealed record ResolutionOption(string Label, int Width, int Height)
{
    public static IReadOnlyList<ResolutionOption> Defaults { get; } =
    [
        new("1920 x 1080 (Full HD)", 1920, 1080),
        new("1280 x 720 (HD)", 1280, 720),
        new("3840 x 2160 (4K)", 3840, 2160),
        new("1080 x 1920 (Vertical, Full HD)", 1080, 1920),
    ];

    public override string ToString() => Label;
}
