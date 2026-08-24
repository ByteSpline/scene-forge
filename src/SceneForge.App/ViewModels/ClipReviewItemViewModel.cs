using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SceneForge.App.Services;
using SceneForge.Media.Detection;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;

namespace SceneForge.App.ViewModels;

// One row in Scene Review's virtualized list. Deliberately lightweight to
// construct - it holds only the already-computed CleanClip/ClipScore facts
// plus a handful of bindable fields, and never loads its thumbnail until
// EnsureThumbnailLoadedCommand actually runs, which
// Behaviors.LazyThumbnailLoader only triggers once the row's container is
// realized by the virtualizing panel (CLAUDE.md rule 6/7: never a bitmap
// per row up front, only for rows actually scrolled into view).
public sealed partial class ClipReviewItemViewModel : ObservableObject
{
    private readonly IThumbnailCacheService _thumbnailCache;
    private readonly string _sourceVideoPath;

    // Stable position in Session.WorkflowSession.ClipOverrides - the
    // combined AcceptedClips-then-RejectedClips index this row was built
    // from (see SceneReviewViewModel).
    public int Key { get; }

    public CleanClip Clip { get; }

    public bool WasAutomaticallyAccepted => Clip.Score.Accepted;

    public IReadOnlyList<ScoreReason> ScoreReasons => Clip.Score.Reasons;

    public TransitionDetection? LeadingTransition { get; }

    public TransitionDetection? TrailingTransition { get; }

    [ObservableProperty]
    private bool isIncluded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBoundaryValid))]
    private TimeSpan adjustedStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBoundaryValid))]
    private TimeSpan adjustedEnd;

    [ObservableProperty]
    private BitmapSource? thumbnail;

    [ObservableProperty]
    private bool isLoadingThumbnail;

    public bool IsBoundaryValid =>
        AdjustedStart >= Clip.Range.Start
        && AdjustedEnd <= Clip.Range.End
        && AdjustedStart < AdjustedEnd;

    // Raised after IsIncluded/AdjustedStart/AdjustedEnd change so
    // SceneReviewViewModel can persist the edit into
    // Session.WorkflowSession.ClipOverrides immediately - every edit
    // survives navigating away from and back to this screen.
    public event EventHandler? OverrideChanged;

    public ClipReviewItemViewModel(
        int key,
        CleanClip clip,
        string sourceVideoPath,
        IThumbnailCacheService thumbnailCache,
        TransitionDetection? leadingTransition,
        TransitionDetection? trailingTransition)
    {
        Key = key;
        Clip = clip;
        _sourceVideoPath = sourceVideoPath;
        _thumbnailCache = thumbnailCache;
        LeadingTransition = leadingTransition;
        TrailingTransition = trailingTransition;

        isIncluded = clip.Score.Accepted;
        adjustedStart = clip.Range.Start;
        adjustedEnd = clip.Range.End;
    }

    [RelayCommand]
    private async Task EnsureThumbnailLoadedAsync()
    {
        if (Thumbnail is not null || IsLoadingThumbnail)
        {
            return;
        }

        IsLoadingThumbnail = true;
        try
        {
            Thumbnail = await _thumbnailCache.GetThumbnailAsync(_sourceVideoPath, Clip.Range.Start, CancellationToken.None);
        }
        finally
        {
            IsLoadingThumbnail = false;
        }
    }

    partial void OnIsIncludedChanged(bool value) => OverrideChanged?.Invoke(this, EventArgs.Empty);

    partial void OnAdjustedStartChanged(TimeSpan value) => OverrideChanged?.Invoke(this, EventArgs.Empty);

    partial void OnAdjustedEndChanged(TimeSpan value) => OverrideChanged?.Invoke(this, EventArgs.Empty);
}
