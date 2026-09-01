using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.App.Navigation;
using SceneForge.App.Persistence;
using SceneForge.App.Session;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Detection;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tooling;
using SceneForge.Media.Validation;

namespace SceneForge.App.ViewModels;

// Step 3: runs the whole analysis half of the pipeline off the UI thread -
// probing (if Welcome/Import somehow skipped it), ITransitionDetector,
// SceneRangeCalculator (pure, synchronous - the Detection-to-Extraction
// bridge, see its own remarks), then ICleanClipExtractor - reporting
// progress and honoring cancellation throughout (CLAUDE.md rule 5). Starts
// itself automatically on construction (see the constructor) rather than
// waiting for a user click, since reaching this screen already committed to
// running analysis from Analysis Settings' "Start analysis" button.
public sealed partial class AnalysisProgressViewModel : ObservableObject, IDisposable
{
    private readonly WorkflowSession _session;
    private readonly IFfprobeService _ffprobeService;
    private readonly ITransitionDetector _transitionDetector;
    private readonly ICleanClipExtractor _cleanClipExtractor;
    private readonly IWorkflowNavigator _navigator;
    private readonly IProjectPersistenceCoordinator _persistence;

    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool isRunning;

    [ObservableProperty]
    private string statusText = "Preparing to analyze...";

    [ObservableProperty]
    private int transitionsFound;

    [ObservableProperty]
    private int clipsAccepted;

    [ObservableProperty]
    private int clipsRejected;

    [ObservableProperty]
    private string? errorMessage;

    public AnalysisProgressViewModel(
        WorkflowSession session,
        IFfprobeService ffprobeService,
        ITransitionDetector transitionDetector,
        ICleanClipExtractor cleanClipExtractor,
        IWorkflowNavigator navigator,
        IProjectPersistenceCoordinator persistence)
    {
        _session = session;
        _ffprobeService = ffprobeService;
        _transitionDetector = transitionDetector;
        _cleanClipExtractor = cleanClipExtractor;
        _navigator = navigator;
        _persistence = persistence;

        _ = RunCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        IsRunning = true;
        ErrorMessage = null;
        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;

        await _persistence.BeginStageAsync(_session, ProjectStage.Analyzed, cancellationToken).ConfigureAwait(true);

        try
        {
            var videoPath = _session.VideoFilePath ?? throw new InvalidOperationException("No source video was imported.");
            var audioPath = _session.AudioFilePath ?? throw new InvalidOperationException("No background audio was imported.");

            StatusText = "Probing source files...";
            _session.VideoMediaInfo ??= await _ffprobeService.ProbeAsync(videoPath, cancellationToken).ConfigureAwait(true);
            _session.AudioMediaInfo ??= await _ffprobeService.ProbeAsync(audioPath, cancellationToken).ConfigureAwait(true);

            StatusText = "Detecting scene transitions...";
            var detectionOptions = TransitionDetectionOptions.ForProfile(_session.AnalysisProfile);
            var detectionProgress = new Progress<TransitionDetectionProgress>(p =>
            {
                // RawCandidatesSoFar is a live activity indicator, not a
                // transition count - every classifier re-scans its sliding
                // window on every sampled frame (see TransitionFuser's own
                // remarks), so this grows into the thousands for any
                // realistic-length video well before fusion collapses it
                // into the real, deduplicated list. Used here only to show
                // the run is progressing; TransitionsFound is corrected to
                // the real count once DetectAsync returns below - see
                // docs/DETECTION_REPORTING_AUDIT.md.
                TransitionsFound = p.RawCandidatesSoFar;
                StatusText = $"Detecting scene transitions... {FormatClock(p.LastSourceTimestamp)} analyzed";
            });
            var detections = await _transitionDetector.DetectAsync(videoPath, _session.VideoMediaInfo!, detectionOptions, detectionProgress, cancellationToken)
                .ConfigureAwait(true);
            _session.Detections = detections;
            TransitionsFound = detections.Count;

            var sceneRangeResult = SceneRangeCalculator.Calculate(_session.VideoMediaInfo!.Duration, detections);
            _session.SceneRangeResult = sceneRangeResult;

            StatusText = "Extracting clean clip candidates...";
            var extractionOptions = CleanClipExtractionOptions.ForProfile(
                _session.AnalysisProfile,
                sceneRangeResult.SceneRanges,
                sceneRangeResult.ExcludedIntervals);
            var extractionProgress = new Progress<CleanClipExtractionProgress>(p =>
            {
                ClipsAccepted = p.ClipsAcceptedSoFar;
                StatusText = $"Extracting clean clip candidates... {FormatClock(p.LastSourceTimestamp)} analyzed";
            });
            var extractionResult = await _cleanClipExtractor.ExtractAsync(videoPath, _session.VideoMediaInfo!, extractionOptions, extractionProgress, cancellationToken)
                .ConfigureAwait(true);
            _session.ExtractionResult = extractionResult;
            ClipsAccepted = extractionResult.AcceptedClips.Count;
            ClipsRejected = extractionResult.RejectedClips.Count;

            await _persistence.CheckpointAsync(_session, ProjectStage.Analyzed, cancellationToken).ConfigureAwait(true);

            StatusText = "Analysis complete.";
            _navigator.NavigateTo(WorkflowStep.SceneReview);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Analysis canceled.";
        }
        catch (Exception ex) when (IsRecognizedAnalysisFailure(ex))
        {
            ErrorMessage = ex.Message;
            StatusText = "Analysis failed.";
        }
        finally
        {
            IsRunning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _cancellationTokenSource?.Cancel();

    private static bool IsRecognizedAnalysisFailure(Exception ex) => ex is
        MediaValidationException or
        FfprobeExecutionException or
        FfmpegToolsNotFoundException or
        FfmpegToolsIncompatibleException or
        TransitionDetectionException or
        CleanClipExtractionException or
        FrameSamplingException or
        ProcessLaunchException or
        InvalidOperationException or
        IOException;

    private static string FormatClock(TimeSpan value) =>
        value.Hours > 0
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    public void Dispose() => _cancellationTokenSource?.Dispose();
}
