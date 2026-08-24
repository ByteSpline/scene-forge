using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.Media.Domain;
using SceneForge.Media.Sampling;

namespace SceneForge.App.ViewModels;

// Step 2: the analysis knobs that must be fixed before analysis runs.
// OutputFrameRate is chosen here, once, and carried unchanged through
// TimelinePlanRequest.OutputTimeBase and RenderOutputSpec.FrameRate later -
// see Session.WorkflowSession's remarks on why it is never re-editable
// downstream.
public sealed partial class AnalysisSettingsViewModel : ObservableObject
{
    private readonly WorkflowSession _session;
    private readonly IWorkflowNavigator _navigator;

    public IReadOnlyList<AnalysisProfile> AvailableProfiles { get; } = Enum.GetValues<AnalysisProfile>();

    public IReadOnlyList<FrameRateOption> AvailableFrameRates { get; } = FrameRateOption.Defaults;

    [ObservableProperty]
    private AnalysisProfile selectedProfile;

    [ObservableProperty]
    private FrameRateOption selectedFrameRate;

    [ObservableProperty]
    private int seed;

    public string VideoSummary { get; }

    public string AudioSummary { get; }

    public AnalysisSettingsViewModel(WorkflowSession session, IWorkflowNavigator navigator)
    {
        _session = session;
        _navigator = navigator;

        selectedProfile = session.AnalysisProfile;
        seed = session.Seed;
        selectedFrameRate = AvailableFrameRates.FirstOrDefault(f => f.Value.Equals(session.OutputFrameRate))
            ?? AvailableFrameRates[2];

        VideoSummary = Describe(session.VideoFilePath, session.VideoMediaInfo);
        AudioSummary = Describe(session.AudioFilePath, session.AudioMediaInfo);
    }

    [RelayCommand]
    private void StartAnalysis()
    {
        _session.AnalysisProfile = SelectedProfile;
        _session.Seed = Seed;
        _session.OutputFrameRate = SelectedFrameRate.Value;
        _navigator.NavigateTo(WorkflowStep.AnalysisProgress);
    }

    private static string Describe(string? path, MediaInfo? info)
    {
        if (path is null || info is null)
        {
            return "Not selected";
        }

        var duration = info.Duration.Hours > 0
            ? info.Duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : info.Duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        return $"{Path.GetFileName(path)} ({duration})";
    }
}

// One selectable output frame rate. ToString() returns Label so a plain
// ComboBox (no DisplayMemberPath) shows the friendly name directly.
public sealed record FrameRateOption(string Label, RationalFrameRate Value)
{
    public static IReadOnlyList<FrameRateOption> Defaults { get; } =
    [
        new("24 fps", new RationalFrameRate(24, 1)),
        new("25 fps", new RationalFrameRate(25, 1)),
        new("30 fps", new RationalFrameRate(30, 1)),
        new("29.97 fps (30000/1001)", new RationalFrameRate(30000, 1001)),
        new("50 fps", new RationalFrameRate(50, 1)),
        new("60 fps", new RationalFrameRate(60, 1)),
    ];

    public override string ToString() => Label;
}
