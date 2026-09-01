using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.App.Navigation;
using SceneForge.App.Persistence;
using SceneForge.App.Session;
using SceneForge.Core.Resources;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Rendering;
using SceneForge.Media.Tooling;

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
    private readonly IProjectPersistenceCoordinator _persistence;

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

    public RenderProgressViewModel(
        WorkflowSession session,
        IFFmpegRenderService renderService,
        IWorkflowNavigator navigator,
        IProjectPersistenceCoordinator persistence)
    {
        _session = session;
        _renderService = renderService;
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

        await _persistence.BeginStageAsync(_session, ProjectStage.Completed, cancellationToken).ConfigureAwait(true);

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

            await _persistence.CheckpointAsync(_session, ProjectStage.Completed, cancellationToken).ConfigureAwait(true);
            await _persistence.FinalizeAsync(cancellationToken).ConfigureAwait(true);

            StatusText = "Render complete.";
            _navigator.NavigateTo(WorkflowStep.Completion);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Render canceled.";
        }
        catch (Exception ex) when (IsRecognizedRenderFailure(ex))
        {
            // A duration-only verification miss never reaches here -
            // FFmpegRenderService self-corrects it internally (see
            // docs/RENDER_DURATION_SELF_CORRECTION.md). Anything that DOES
            // reach here is a genuinely unrecoverable environment or
            // content-integrity problem, so it is still shown - but as a
            // calm, plain-language message rather than the raw exception
            // text, per the product requirement that this screen must never
            // look like a crash.
            ErrorMessage = BuildFriendlyErrorMessage(ex);
            StatusText = "We couldn't finish your render.";
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
        FfmpegToolsNotFoundException or
        FfmpegToolsIncompatibleException or
        InvalidOperationException or
        IOException or
        UnauthorizedAccessException;

    // Calm, non-technical, plain-language text for every recognized render
    // failure - never the raw exception message (which can read like a
    // crash: exit codes, ffmpeg stderr excerpts, stack-trace-adjacent
    // phrasing). Each still explains what's wrong and what the user can do,
    // per the product requirement that even a genuinely unrecoverable
    // failure must not look like the app broke.
    private static string BuildFriendlyErrorMessage(Exception ex) => ex switch
    {
        InsufficientDiskSpaceException => "There isn't enough free disk space to finish this render. Free up some space on your drive and try again.",
        RenderVerificationException => "SceneForge couldn't produce a fully valid video from your files. This usually means there's a problem with the source video or audio - try re-importing your files, or choosing different ones, and render again.",
        RenderExecutionException => "Something went wrong while creating your video. Please try rendering again - if this keeps happening, check that your video and audio files aren't corrupted, or restart SceneForge.",
        RenderPlanException => "SceneForge couldn't prepare your video for rendering. Go back to Export Settings, double-check your choices, and try again.",
        FfmpegToolsNotFoundException or FfmpegToolsIncompatibleException => "SceneForge's video engine couldn't be started. Try reinstalling the app; if that doesn't help, contact support.",
        UnauthorizedAccessException => "SceneForge doesn't have permission to write to the selected output location. Choose a different folder and try again.",
        IOException => "SceneForge couldn't read or write one of the files needed for this render. Check that your files aren't open in another program, then try again.",
        InvalidOperationException => "Something isn't ready yet. Go back to Export Settings and try again.",
        _ => "Something went wrong while creating your video. Please try again.",
    };

    private static string FormatClock(TimeSpan value) =>
        value.Hours > 0
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    public void Dispose() => _cancellationTokenSource?.Dispose();
}
