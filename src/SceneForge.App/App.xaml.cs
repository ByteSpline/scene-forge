using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SceneForge.App.Navigation;
using SceneForge.App.Persistence;
using SceneForge.App.Services;
using SceneForge.App.Session;
using SceneForge.App.ViewModels;
using SceneForge.Core.Resources;
using SceneForge.Infrastructure.Logging;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Detection;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Rendering;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tooling;

namespace SceneForge.App;

// Composition root: builds the DI container and resolves the shell window.
// Every SceneForge.Media interface used anywhere in the App layer is
// registered exactly once, here - no ViewModel or service ever calls `new`
// on a Media implementation type directly (CLAUDE.md rule 4: UI concerns
// stay separate from, and only ever depend inward on, core/processing
// logic).
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplySystemColorTheme();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        // Fired only after the window is visible and this method has
        // returned control to the dispatcher's message loop - never
        // blocking startup itself. StartupRecoveryRunner.RunAsync awaits
        // every I/O call (including a real ffprobe re-probe, bounded by its
        // own RecoveryProbeTimeout) and catches everything it does not
        // already expect, so this is safe to fire and forget (CLAUDE.md
        // rule 5 - see StartupRecoveryRunner's remarks for why this used to
        // block here, synchronously, before the window existed at all).
        _ = StartupRecoveryRunner.RunAsync(_serviceProvider);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Shared adaptive resource control (bounded worker counts, disk-space
        // guards) - lives in SceneForge.Core so both the Media pipeline and
        // App-layer services below can depend on the same instance without
        // Media needing a reference to Infrastructure (see
        // IAdaptiveResourceGovernor's own remarks on the reference graph).
        services.AddSingleton<IAdaptiveResourceGovernor, AdaptiveResourceGovernor>();

        // SceneForge.Media pipeline - the same real implementations every
        // prior phase's tests exercise. Registered as singletons: none of
        // them hold per-run mutable state (each call takes its own request/
        // options record), so sharing one instance across the whole app
        // lifetime is safe and avoids re-locating ffmpeg/ffprobe on every
        // screen.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IFfmpegToolLocator, FfmpegToolLocator>();
        services.AddSingleton<IFfprobeService, FfprobeService>();
        services.AddSingleton<IFrameSampler, FrameSampler>();
        services.AddSingleton<ITransitionDetector, TransitionDetector>();
        services.AddSingleton<ICleanClipExtractor, CleanClipExtractor>();
        services.AddSingleton<ITimelinePlanner, TimelinePlanner>();
        services.AddSingleton<IRenderPlanBuilder, RenderPlanBuilder>();
        services.AddSingleton<IFFmpegRenderService, FFmpegRenderService>();

        // App-layer services.
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThumbnailCacheService, ThumbnailCacheService>();
        services.AddSingleton<IWorkflowNavigator, WorkflowNavigator>();
        services.AddSingleton<WorkflowSession>();

        // Local project persistence (Phase 11) - one app-owned root under
        // %LOCALAPPDATA%\SceneForge holds every project checkpoint, temp
        // file, and log file this app ever writes; nothing here is a
        // user-selected path (CLAUDE.md rule 12 only governs render output,
        // which stays exactly as user-chosen as before - see
        // ExportSettingsViewModel).
        var layout = new ProjectLayout(ProjectLayout.DefaultAppDataRoot);
        services.AddSingleton(layout);
        services.AddSingleton<ITempFileRegistry>(new TempFileRegistry(layout.TempRoot));
        services.AddSingleton<IAppLogger>(new RollingFileLogger(layout.LogsRoot));
        services.AddSingleton<IProjectStore, ProjectStore>();
        services.AddSingleton<IStaleSourceDetector, StaleSourceDetector>();
        services.AddSingleton<IAutosaveService, AutosaveService>();
        services.AddSingleton<IProjectRecoveryService, ProjectRecoveryService>();
        services.AddSingleton<IProjectPersistenceCoordinator, ProjectPersistenceCoordinator>();

        // Shell.
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        // One workflow step = one transient ViewModel, re-created from the
        // shared WorkflowSession on every navigation (see
        // MainWindowViewModel.OnStepChanged).
        services.AddTransient<WelcomeImportViewModel>();
        services.AddTransient<AnalysisSettingsViewModel>();
        services.AddTransient<AnalysisProgressViewModel>();
        services.AddTransient<SceneReviewViewModel>();
        services.AddTransient<TimelineSummaryViewModel>();
        services.AddTransient<ExportSettingsViewModel>();
        services.AddTransient<RenderProgressViewModel>();
        services.AddTransient<CompletionViewModel>();
    }

    // Picked once at startup from the Windows "AppsUseLightTheme" setting -
    // not re-evaluated while running (see docs/PHASE_10_REPORT.md, Known
    // limitations). Every color in Themes/Styles.xaml and every View
    // resolves through Colors.Light.xaml/Colors.Dark.xaml's shared brush
    // keys via DynamicResource, so whichever is inserted here determines the
    // whole application's palette consistently.
    private static void ApplySystemColorTheme()
    {
        var themeUri = new Uri(
            IsLightThemeActive() ? "Themes/Colors.Light.xaml" : "Themes/Colors.Dark.xaml",
            UriKind.Relative);

        Current.Resources.MergedDictionaries.Insert(0, new ResourceDictionary { Source = themeUri });
    }

    private static bool IsLightThemeActive()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _serviceProvider?.GetService<IDialogService>()?.ShowError(
            "SceneForge encountered an unexpected error",
            e.Exception.Message);
        e.Handled = true;
    }
}
