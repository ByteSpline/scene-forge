using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.Media.Tooling;

namespace SceneForge.App.ViewModels;

// Backs StartupDiagnosticsWindow, shown modally by App.OnStartup before
// MainWindow ever appears (see that method's remarks): runs
// INativeDependencyDiagnosticsService.RunAsync automatically on
// construction, same pattern AnalysisProgressViewModel uses for its own
// self-starting work, and exposes AllPassed so the window can gate its
// "Continue" button on it rather than letting a user reach the workflow
// with a native dependency it already knows is broken.
public sealed partial class StartupDiagnosticsViewModel : ObservableObject
{
    private readonly INativeDependencyDiagnosticsService _diagnosticsService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool isRunning;

    [ObservableProperty]
    private bool hasRun;

    [ObservableProperty]
    private bool allPassed;

    public ObservableCollection<NativeComponentCheckResult> Results { get; } = [];

    public StartupDiagnosticsViewModel(INativeDependencyDiagnosticsService diagnosticsService)
    {
        _diagnosticsService = diagnosticsService;

        _ = RetryCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RetryAsync()
    {
        IsRunning = true;
        AllPassed = false;
        Results.Clear();

        // No cancellation surface here deliberately: every individual check
        // this composes (FfmpegToolLocator.LocateAsync, the VC++ runtime
        // probe, the OpenCV probe) is a fast, bounded, local-only operation
        // - none of them can hang indefinitely the way a render or an
        // analysis pass can, so there is nothing a user would need to
        // cancel out of (CLAUDE.md rule 5 targets long-running/blocking
        // work; this is neither).
        var report = await _diagnosticsService.RunAsync(CancellationToken.None).ConfigureAwait(true);

        foreach (var result in report.Results)
        {
            Results.Add(result);
        }

        AllPassed = report.AllPassed;
        HasRun = true;
        IsRunning = false;
    }

    private bool CanRun() => !IsRunning;
}
