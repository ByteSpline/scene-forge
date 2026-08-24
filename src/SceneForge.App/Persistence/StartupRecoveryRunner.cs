using System.Globalization;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SceneForge.App.Navigation;
using SceneForge.App.Services;
using SceneForge.App.Session;
using SceneForge.Infrastructure.Logging;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Probing;
using SceneForge.Media.Tooling;
using SceneForge.Media.Validation;

namespace SceneForge.App.Persistence;

// Runs once at startup (see App.OnStartup, which schedules RunAsync onto the
// Dispatcher AFTER the shell window is already shown and its message loop is
// pumping - never before, and never blocking the thread that owns that loop):
// sweeps any orphaned app-owned temp file left by a process that died before
// it could clean up after itself (CLAUDE.md rule 11), then offers to resume
// any project whose in-progress marker was never cleared by a matching
// AutosaveService.CompleteStageAsync call - i.e. the previous run did not
// shut down cleanly while working on it.
//
// Every I/O call in here - including the one real child-process invocation,
// IFfprobeService.ProbeAsync - is awaited (never `.GetAwaiter().GetResult()`
// blocking a thread the UI depends on) and threaded with a genuine,
// externally-cancellable CancellationToken bounded by RecoveryProbeTimeout,
// independent of whatever internal default timeout FfprobeService itself
// happens to have (CLAUDE.md rule 5 - a long-running, potentially-blocking
// operation must support cooperative cancellation, not merely "eventually
// stop on its own").
//
// A resumed project always lands on Welcome/Import, never jumps straight
// into the middle of the eight-screen workflow: the persisted schema
// deliberately does not carry every derived pipeline artifact (extraction
// results with full perceptual descriptors, a built TimelinePlan/RenderPlan
// - see SceneForgeProjectDocument's remarks), so re-entering at the first
// screen and letting each later stage recompute its own output is more
// honest than fabricating a deeper resume the persisted data cannot
// actually back up. What IS restored: the source paths (re-probed
// immediately if IStaleSourceDetector confirms they are unchanged),
// analysis profile, output frame rate, shuffle seed, and export settings -
// so a user resuming a genuinely unmodified source can reach Continue on
// Welcome/Import without re-picking anything.
public static class StartupRecoveryRunner
{
    // Bounds each recovery-time re-probe independently of FfprobeService's
    // own internal default (currently 30s) - a deliberately shorter,
    // App-layer-owned bound, since this runs as background work after the
    // window is already visible and a slow/hung probe here should give up
    // and fall back to "please re-import" well before a user would
    // reasonably wait on it.
    public static readonly TimeSpan RecoveryProbeTimeout = TimeSpan.FromSeconds(20);

    public static async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var logger = serviceProvider.GetRequiredService<IAppLogger>();

        try
        {
            await RunCoreAsync(serviceProvider, logger, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // This runs fire-and-forget from App.OnStartup (see that
            // method's remarks) with no other error surface - an
            // unanticipated failure anywhere in this method must never
            // propagate as an unobserved exception that could tear down the
            // process. Every failure this method already expects is caught
            // more specifically, and logged more specifically, below; this
            // is only the last-resort safety net.
            logger.Log(LogLevel.Error, "Startup project recovery failed unexpectedly.", ex);
        }
    }

    private static async Task RunCoreAsync(IServiceProvider serviceProvider, IAppLogger logger, CancellationToken cancellationToken)
    {
        var tempFileRegistry = serviceProvider.GetRequiredService<ITempFileRegistry>();

        try
        {
            await tempFileRegistry.SweepOrphansAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsRecognizedIoFailure(ex))
        {
            logger.Log(LogLevel.Warning, "Startup temp-file sweep failed.", ex);
        }

        var recoveryService = serviceProvider.GetRequiredService<IProjectRecoveryService>();

        IReadOnlyList<RecoverableProject> recoverableProjects;
        try
        {
            recoverableProjects = await recoveryService.ScanForInterruptedProjectsAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsRecognizedIoFailure(ex))
        {
            logger.Log(LogLevel.Warning, "Startup project-recovery scan failed.", ex);
            return;
        }

        if (recoverableProjects.Count == 0)
        {
            return;
        }

        var dialogService = serviceProvider.GetRequiredService<IDialogService>();
        var session = serviceProvider.GetRequiredService<WorkflowSession>();
        var navigator = serviceProvider.GetRequiredService<IWorkflowNavigator>();
        var staleSourceDetector = serviceProvider.GetRequiredService<IStaleSourceDetector>();
        var ffprobeService = serviceProvider.GetRequiredService<IFfprobeService>();

        foreach (var project in recoverableProjects)
        {
            if (!OfferRecovery(project, dialogService))
            {
                await recoveryService.DiscardAsync(project.ProjectId, cancellationToken).ConfigureAwait(true);
                continue;
            }

            if (project.LastCheckpoint is not { } checkpoint)
            {
                // Interrupted before its very first checkpoint - nothing to
                // actually restore into the session; just clear the marker
                // so this project stops being reported every startup.
                await recoveryService.DiscardAsync(project.ProjectId, cancellationToken).ConfigureAwait(true);
                continue;
            }

            await ApplyCheckpointToSessionAsync(checkpoint, session, staleSourceDetector, ffprobeService, dialogService, logger, cancellationToken).ConfigureAwait(true);
            session.ProjectId = project.ProjectId;
            await recoveryService.DiscardAsync(project.ProjectId, cancellationToken).ConfigureAwait(true);
            navigator.Reset();

            // WorkflowSession holds exactly one active project - once one
            // recoverable project has actually been restored into it, stop
            // offering the rest for this startup rather than overwriting
            // the session again; any remaining interrupted projects still
            // have their markers intact and are offered again next launch.
            break;
        }
    }

    private static bool OfferRecovery(RecoverableProject project, IDialogService dialogService)
    {
        var checkpoint = project.LastCheckpoint;
        var description = checkpoint is null
            ? $"SceneForge found a project that was started but never reached its first saved checkpoint (interrupted {FormatTimestamp(project.InterruptedAtUtc)})."
            : $"SceneForge found a project last saved after completing stage '{checkpoint.Stage}' " +
              $"(source: {Path.GetFileName(checkpoint.VideoSource.FilePath)}), interrupted while starting stage " +
              $"'{project.InterruptedStage}' ({FormatTimestamp(project.InterruptedAtUtc)}). This usually means SceneForge did not shut down cleanly.";

        return dialogService.ShowConfirmation(
            "Resume interrupted project?",
            $"{description}\n\nResume it now (you will return to Welcome/Import with your source files and settings restored), or discard and start fresh?");
    }

    internal static async Task ApplyCheckpointToSessionAsync(
        SceneForgeProjectDocument checkpoint,
        WorkflowSession session,
        IStaleSourceDetector staleSourceDetector,
        IFfprobeService ffprobeService,
        IDialogService dialogService,
        IAppLogger logger,
        CancellationToken cancellationToken)
    {
        session.AnalysisProfile = checkpoint.AnalysisProfile ?? session.AnalysisProfile;
        session.OutputFrameRate = checkpoint.OutputFrameRate ?? session.OutputFrameRate;
        session.Seed = checkpoint.TimelineSeed ?? session.Seed;

        if (checkpoint.RenderSettings is { } renderSettings)
        {
            session.FitMode = renderSettings.FitMode;
            session.OutputWidth = renderSettings.OutputWidth;
            session.OutputHeight = renderSettings.OutputHeight;
            session.OutputVideoPath = renderSettings.OutputVideoPath;
        }

        await RestoreSourceIfFreshAsync(
            checkpoint.VideoSource,
            "source video",
            staleSourceDetector,
            ffprobeService,
            dialogService,
            logger,
            path => session.VideoFilePath = path,
            info => session.VideoMediaInfo = info,
            cancellationToken).ConfigureAwait(true);

        if (checkpoint.AudioSource is { } audioSource)
        {
            await RestoreSourceIfFreshAsync(
                audioSource,
                "background audio track",
                staleSourceDetector,
                ffprobeService,
                dialogService,
                logger,
                path => session.AudioFilePath = path,
                info => session.AudioMediaInfo = info,
                cancellationToken).ConfigureAwait(true);
        }
    }

    internal static async Task RestoreSourceIfFreshAsync(
        SourceFingerprint fingerprint,
        string sourceDescription,
        IStaleSourceDetector staleSourceDetector,
        IFfprobeService ffprobeService,
        IDialogService dialogService,
        IAppLogger logger,
        Action<string> setPath,
        Action<Media.Domain.MediaInfo> setMediaInfo,
        CancellationToken cancellationToken,
        TimeSpan? probeTimeout = null)
    {
        var freshness = staleSourceDetector.CheckFreshness(fingerprint);
        if (freshness.Status != SourceFreshnessStatus.Fresh)
        {
            dialogService.ShowError(
                "Recovered project source has changed",
                $"The {sourceDescription} recorded in this project could not be restored: {freshness.Message} Please re-import it on Welcome/Import.");
            return;
        }

        setPath(fingerprint.FilePath);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(probeTimeout ?? RecoveryProbeTimeout);

        try
        {
            var info = await ffprobeService.ProbeAsync(fingerprint.FilePath, timeoutCts.Token).ConfigureAwait(true);
            setMediaInfo(info);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own RecoveryProbeTimeout fired, not the caller's token -
            // a slow/stalled probe during startup recovery degrades to
            // "please re-import," never an unhandled exception or an
            // indefinite wait.
            logger.Log(LogLevel.Warning, $"Re-probing recovered {sourceDescription} '{fingerprint.FilePath}' did not finish within {probeTimeout ?? RecoveryProbeTimeout}; leaving it unset.");
        }
        catch (Exception ex) when (IsRecognizedProbeFailure(ex))
        {
            logger.Log(LogLevel.Warning, $"Could not re-probe recovered {sourceDescription} '{fingerprint.FilePath}'.", ex);
        }
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value == DateTimeOffset.MinValue
            ? "time unknown"
            : value.ToLocalTime().ToString("g", CultureInfo.InvariantCulture);

    private static bool IsRecognizedProbeFailure(Exception ex) => ex is
        MediaValidationException or
        FfprobeExecutionException or
        FfmpegToolsNotFoundException or
        FfmpegToolsIncompatibleException or
        IOException;

    private static bool IsRecognizedIoFailure(Exception ex) => ex is
        IOException or
        UnauthorizedAccessException or
        ProjectPersistenceException;
}
