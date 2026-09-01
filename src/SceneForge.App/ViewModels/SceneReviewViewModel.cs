using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.App.Navigation;
using SceneForge.App.Persistence;
using SceneForge.App.Services;
using SceneForge.App.Session;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;

namespace SceneForge.App.ViewModels;

// Step 4: every candidate clip CleanClipExtractor produced (accepted AND
// rejected - rejection is never hidden, CLAUDE.md rule 10), with per-clip
// include/exclude and boundary-adjustment controls. Backed by a virtualized
// ListView (see Views/SceneReviewView.xaml - VirtualizingPanel.VirtualizationMode
// "Recycling") so a source with thousands of candidate clips never
// instantiates thousands of live thumbnails/containers at once (CLAUDE.md
// rule 6/7).
public sealed partial class SceneReviewViewModel : ObservableObject
{
    private readonly WorkflowSession _session;
    private readonly IWorkflowNavigator _navigator;
    private readonly IProjectPersistenceCoordinator _persistence;

    public ObservableCollection<ClipReviewItemViewModel> Clips { get; } = [];

    [ObservableProperty]
    private int includedCount;

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private string? errorMessage;

    public SceneReviewViewModel(
        WorkflowSession session,
        IThumbnailCacheService thumbnailCache,
        IWorkflowNavigator navigator,
        IProjectPersistenceCoordinator persistence)
    {
        _session = session;
        _navigator = navigator;
        _persistence = persistence;

        var extraction = session.ExtractionResult;
        var videoPath = session.VideoFilePath;
        if (extraction is null || videoPath is null)
        {
            ErrorMessage = "No analysis results are available. Go back and run analysis first.";
            return;
        }

        var combined = new List<CleanClip>(extraction.AcceptedClips.Count + extraction.RejectedClips.Count);
        combined.AddRange(extraction.AcceptedClips);
        combined.AddRange(extraction.RejectedClips);

        for (var i = 0; i < combined.Count; i++)
        {
            var clip = combined[i];
            var boundary = FindBoundary(session.SceneRangeResult, clip.SourceSceneIndex);
            var overrideEntry = session.ClipOverrides.GetValueOrDefault(i);

            var item = new ClipReviewItemViewModel(
                i,
                clip,
                videoPath,
                thumbnailCache,
                boundary?.Leading,
                boundary?.Trailing)
            {
                IsIncluded = overrideEntry?.IsIncluded ?? clip.Score.Accepted,
                AdjustedStart = overrideEntry?.AdjustedRange.Start ?? clip.Range.Start,
                AdjustedEnd = overrideEntry?.AdjustedRange.End ?? clip.Range.End,
            };
            item.OverrideChanged += OnClipOverrideChanged;
            Clips.Add(item);
        }

        TotalCount = Clips.Count;
        RecomputeIncludedCount();
    }



    [RelayCommand]
    private void IncludeAll()
    {
        foreach (var clip in Clips)
        {
            clip.IsIncluded = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task Continue()
    {
        _session.ReviewedClips = Clips
            .Where(c => c.IsIncluded && c.IsBoundaryValid)
            .Select(c => c.Clip with { Range = new TimeRange(c.AdjustedStart, c.AdjustedEnd) })
            .ToList();

        await _persistence.CheckpointAsync(_session, ProjectStage.Reviewed);

        _navigator.NavigateTo(WorkflowStep.TimelineSummary);
    }

    private bool CanContinue() => Clips.Any(c => c.IsIncluded && c.IsBoundaryValid);

    private static SceneBoundaryTransitions? FindBoundary(SceneRangeCalculationResult? sceneRangeResult, int sourceSceneIndex)
    {
        var boundaries = sceneRangeResult?.BoundaryTransitions;
        if (boundaries is null || sourceSceneIndex < 0 || sourceSceneIndex >= boundaries.Count)
        {
            return null;
        }

        return boundaries[sourceSceneIndex];
    }

    private void OnClipOverrideChanged(object? sender, EventArgs e)
    {
        if (sender is not ClipReviewItemViewModel item)
        {
            return;
        }

        _session.ClipOverrides[item.Key] = new ClipReviewOverride
        {
            IsIncluded = item.IsIncluded,
            AdjustedRange = item.IsBoundaryValid
                ? new TimeRange(item.AdjustedStart, item.AdjustedEnd)
                : item.Clip.Range,
        };

        RecomputeIncludedCount();
        ContinueCommand.NotifyCanExecuteChanged();
    }

    private void RecomputeIncludedCount() => IncludedCount = Clips.Count(c => c.IsIncluded);
}
