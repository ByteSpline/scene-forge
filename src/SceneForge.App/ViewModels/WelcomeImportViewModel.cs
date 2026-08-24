using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.App.Navigation;
using SceneForge.App.Services;
using SceneForge.App.Session;
using SceneForge.Media.Domain;
using SceneForge.Media.Probing;
using SceneForge.Media.Tooling;
using SceneForge.Media.Validation;

namespace SceneForge.App.ViewModels;

// Step 1: import a source video and a background audio track (both are
// required - TimelinePlanRequest.TargetAudioDuration and
// RenderAudioTrackSpec.FilePath both need a real audio file, so it is
// collected here rather than deferred to Export Settings; see
// Session.WorkflowSession's remarks). Each file is probed with
// IFfprobeService immediately on selection/drop so a bad file (wrong
// format, no video/audio stream, corrupt) surfaces here rather than several
// screens later.
public sealed partial class WelcomeImportViewModel : ObservableObject
{
    private readonly WorkflowSession _session;
    private readonly IDialogService _dialogService;
    private readonly IFfprobeService _ffprobeService;
    private readonly IWorkflowNavigator _navigator;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string? videoFilePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string? audioFilePath;

    [ObservableProperty]
    private string? videoSummary;

    [ObservableProperty]
    private string? audioSummary;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool isProbingVideo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool isProbingAudio;

    [ObservableProperty]
    private string? errorMessage;

    public WelcomeImportViewModel(
        WorkflowSession session,
        IDialogService dialogService,
        IFfprobeService ffprobeService,
        IWorkflowNavigator navigator)
    {
        _session = session;
        _dialogService = dialogService;
        _ffprobeService = ffprobeService;
        _navigator = navigator;

        VideoFilePath = session.VideoFilePath;
        AudioFilePath = session.AudioFilePath;
        VideoSummary = Describe(session.VideoMediaInfo, session.VideoFilePath);
        AudioSummary = Describe(session.AudioMediaInfo, session.AudioFilePath);
    }

    [RelayCommand]
    private async Task BrowseVideoAsync()
    {
        var path = _dialogService.ShowOpenVideoFileDialog();
        if (path is not null)
        {
            await ImportVideoAsync(path);
        }
    }

    [RelayCommand]
    private async Task BrowseAudioAsync()
    {
        var path = _dialogService.ShowOpenAudioFileDialog();
        if (path is not null)
        {
            await ImportAudioAsync(path);
        }
    }

    // Invoked both by BrowseVideoCommand and by Behaviors.DragDropImportBehavior
    // bound to VideoImportCommand from the drop zone in WelcomeImportView.
    [RelayCommand]
    private async Task VideoImportAsync(string? path)
    {
        if (path is not null)
        {
            await ImportVideoAsync(path);
        }
    }

    [RelayCommand]
    private async Task AudioImportAsync(string? path)
    {
        if (path is not null)
        {
            await ImportAudioAsync(path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        _session.VideoFilePath = VideoFilePath;
        _session.AudioFilePath = AudioFilePath;
        _navigator.NavigateTo(WorkflowStep.AnalysisSettings);
    }

    private bool CanContinue() =>
        !IsProbingVideo && !IsProbingAudio
        && _session.VideoMediaInfo is not null
        && _session.AudioMediaInfo is not null;

    private async Task ImportVideoAsync(string path)
    {
        ErrorMessage = null;
        IsProbingVideo = true;
        _session.VideoMediaInfo = null;
        try
        {
            var validatedPath = MediaPathValidator.ValidateInputFile(path);
            var info = await _ffprobeService.ProbeAsync(validatedPath, CancellationToken.None);
            if (info.PrimaryVideoStream is null)
            {
                throw new MediaValidationException(MediaValidationFailureReason.NoMediaStreams, $"'{Path.GetFileName(validatedPath)}' does not contain a video stream.");
            }

            VideoFilePath = validatedPath;
            _session.VideoFilePath = validatedPath;
            _session.VideoMediaInfo = info;
            VideoSummary = Describe(info, validatedPath);
        }
        catch (Exception ex) when (IsRecognizedImportFailure(ex))
        {
            ErrorMessage = ex.Message;
            VideoSummary = null;
        }
        finally
        {
            IsProbingVideo = false;
        }
    }

    private async Task ImportAudioAsync(string path)
    {
        ErrorMessage = null;
        IsProbingAudio = true;
        _session.AudioMediaInfo = null;
        try
        {
            var validatedPath = MediaPathValidator.ValidateInputFile(path);
            var info = await _ffprobeService.ProbeAsync(validatedPath, CancellationToken.None);
            if (info.PrimaryAudioStream is null)
            {
                throw new MediaValidationException(MediaValidationFailureReason.NoMediaStreams, $"'{Path.GetFileName(validatedPath)}' does not contain an audio stream.");
            }

            AudioFilePath = validatedPath;
            _session.AudioFilePath = validatedPath;
            _session.AudioMediaInfo = info;
            AudioSummary = Describe(info, validatedPath);
        }
        catch (Exception ex) when (IsRecognizedImportFailure(ex))
        {
            ErrorMessage = ex.Message;
            AudioSummary = null;
        }
        finally
        {
            IsProbingAudio = false;
        }
    }

    private static bool IsRecognizedImportFailure(Exception ex) => ex is
        MediaValidationException or
        FfprobeExecutionException or
        FfmpegToolsNotFoundException or
        FfmpegToolsIncompatibleException;

    private static string? Describe(MediaInfo? info, string? path)
    {
        if (info is null || path is null)
        {
            return null;
        }

        return $"{Path.GetFileName(path)} — {FormatDuration(info.Duration)}";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.Hours > 0
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
