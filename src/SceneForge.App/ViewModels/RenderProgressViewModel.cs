using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.Media.Rendering;

namespace SceneForge.App.ViewModels;

// Step 7: runs FFmpegRenderService.RenderAsync against the RenderPlan built
// in Export Settings, off the UI thread, reporting ffmpeg's own progress
// stream (never scraped from log text - see RenderProgress) and honoring
// cancellation (CLAUDE.md rule 5). Starts itself on construction, the same
// pattern AnalysisProgressViewModel uses.
public sealed partial class RenderProgressViewModel : ObservableObject, IDisposable
{
    private readonly WorkflowSession _session;
    private readonly IFFmpegRenderService _renderService;
    private readonly IWorkflowNavigator _navigator;

    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool isRunning;

    [ObservableProperty]
    private string statusText = "Preparing to render...";

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private TimeSpan? estimatedTimeRemaining;

    [ObservableProperty]
    private double? speedMultiplier;

    [ObservableProperty]
    private string? errorMessage;

    public RenderProgressViewModel(WorkflowSession session, IFFmpegRenderService renderService, IWorkflowNavigator navigator)
    {
        _session = session;
        _renderService = renderService;
        _navigator = navigator;

        _ = RunCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        IsRunning = true;
        ErrorMessage = null;
        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;

        try
        {
            var plan = _session.RenderPlan ?? throw new InvalidOperationException("No render plan is available. Go back to Export Settings.");
            var outputPath = _session.OutputVideoPath ?? throw new InvalidOperationException("No output path was chosen.");

            var progress = new Progress<RenderProgress>(p =>
            {
                var total = plan.PlannedVideoDuration;
                ProgressPercent = total > TimeSpan.Zero
                    ? Math.Clamp(p.OutTime.TotalSeconds / total.TotalSeconds * 100.0, 0, 100)
                    : 0;
                EstimatedTimeRemaining = p.EstimatedTimeRemaining;
                SpeedMultiplier = p.Speed;
                StatusText = $"Rendering... {FormatClock(p.OutTime)} of {FormatClock(total)}";
            });

            var result = await _renderService.RenderAsync(plan, outputPath, progress, cancellationToken).ConfigureAwait(true);
            _session.RenderResult = result;
            StatusText = "Render complete.";
            _navigator.NavigateTo(WorkflowStep.Completion);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Render canceled.";
        }
        catch (Exception ex) when (IsRecognizedRenderFailure(ex))
        {
            ErrorMessage = ex.Message;
            StatusText = "Render failed.";
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

    private static bool IsRecognizedRenderFailure(Exception ex) => ex is
        RenderExecutionException or
        RenderVerificationException or
        RenderPlanException or
        InvalidOperationException or
        IOException or
        UnauthorizedAccessException;

    private static string FormatClock(TimeSpan value) =>
        value.Hours > 0
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    public void Dispose() => _cancellationTokenSource?.Dispose();
}
