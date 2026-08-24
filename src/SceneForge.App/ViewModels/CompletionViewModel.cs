using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.App.Navigation;
using SceneForge.App.Services;
using SceneForge.App.Session;

namespace SceneForge.App.ViewModels;

// Step 8: the render's own itemized verification result (never a bare
// "success" - CLAUDE.md rule 10), which encoder actually ran, and whether a
// hardware encoder failed mid-render and fell back to libx264 (see
// RenderResult.FellBackToSoftwareEncoder). "Start over" resets both
// navigation history and session state so a second run never inherits any
// stale value from this one.
public sealed partial class CompletionViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    private readonly WorkflowSession _session;
    private readonly IWorkflowNavigator _navigator;

    public string OutputFilePath { get; }

    public string EncoderDescription { get; }

    public bool FellBackToSoftwareEncoder { get; }

    public TimeSpan Elapsed { get; }

    public bool VerificationPassed { get; }

    public IReadOnlyList<string> VerificationFailures { get; }

    public CompletionViewModel(WorkflowSession session, IDialogService dialogService, IWorkflowNavigator navigator)
    {
        _session = session;
        _dialogService = dialogService;
        _navigator = navigator;

        var result = session.RenderResult;
        OutputFilePath = result?.OutputFilePath ?? session.OutputVideoPath ?? string.Empty;
        EncoderDescription = result is null
            ? "Unknown"
            : $"{result.Encoder.FfmpegEncoderName} ({(result.Encoder.IsHardwareAccelerated ? "hardware-accelerated" : "software")})";
        FellBackToSoftwareEncoder = result?.FellBackToSoftwareEncoder ?? false;
        Elapsed = result?.Elapsed ?? TimeSpan.Zero;
        VerificationPassed = result?.Verification.IsValid ?? false;
        VerificationFailures = result?.Verification.Failures ?? [];
    }

    [RelayCommand]
    private void OpenOutputFolder() => _dialogService.RevealInFileExplorer(OutputFilePath);

    [RelayCommand]
    private void StartOver()
    {
        _session.Reset();
        _navigator.Reset();
    }
}
