using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;
using SceneForge.Media.Extraction;

namespace SceneForge.App.Tests.ViewModels;

public class SceneReviewViewModelTests
{
    [Fact]
    public void Constructor_NoExtractionResult_SetsErrorMessageAndEmptyList()
    {
        var session = new WorkflowSession { VideoFilePath = "video.mp4" };

        var vm = new SceneReviewViewModel(session, new FakeThumbnailCacheService(), new WorkflowNavigator());

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(vm.Clips);
    }

    [Fact]
    public void Constructor_WithAcceptedAndRejectedClips_BuildsCombinedListDefaultingToAutomaticVerdict()
    {
        var session = BuildSessionWithClips(out var accepted, out var rejected);

        var vm = new SceneReviewViewModel(session, new FakeThumbnailCacheService(), new WorkflowNavigator());

        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(1, vm.IncludedCount);
        Assert.True(vm.Clips[0].IsIncluded);
        Assert.False(vm.Clips[1].IsIncluded);
        Assert.Same(accepted, vm.Clips[0].Clip);
        Assert.Same(rejected, vm.Clips[1].Clip);
    }

    [Fact]
    public void TogglingIsIncluded_PersistsOverrideIntoSessionAndUpdatesCount()
    {
        var session = BuildSessionWithClips(out _, out _);
        var vm = new SceneReviewViewModel(session, new FakeThumbnailCacheService(), new WorkflowNavigator());

        vm.Clips[1].IsIncluded = true;

        Assert.Equal(2, vm.IncludedCount);
        Assert.True(session.ClipOverrides[1].IsIncluded);
    }

    [Fact]
    public void AdjustingBoundary_WithinRange_PersistsAdjustedRange()
    {
        var session = BuildSessionWithClips(out var accepted, out _);
        var vm = new SceneReviewViewModel(session, new FakeThumbnailCacheService(), new WorkflowNavigator());

        vm.Clips[0].AdjustedStart = accepted.Range.Start + TimeSpan.FromSeconds(1);

        var stored = session.ClipOverrides[0];
        Assert.Equal(accepted.Range.Start + TimeSpan.FromSeconds(1), stored.AdjustedRange.Start);
    }

    [Fact]
    public void AdjustingBoundary_MadeInvalid_FallsBackToOriginalClipRangeInSession()
    {
        var session = BuildSessionWithClips(out var accepted, out _);
        var vm = new SceneReviewViewModel(session, new FakeThumbnailCacheService(), new WorkflowNavigator());

        // Start after end makes the boundary invalid.
        vm.Clips[0].AdjustedStart = accepted.Range.End + TimeSpan.FromSeconds(5);

        Assert.False(vm.Clips[0].IsBoundaryValid);
        Assert.Equal(accepted.Range, session.ClipOverrides[0].AdjustedRange);
    }

    [Fact]
    public void ContinueCommand_CanExecute_OnlyWhenAtLeastOneIncludedValidClipExists()
    {
        var session = BuildSessionWithClips(out _, out _);
        var vm = new SceneReviewViewModel(session, new FakeThumbnailCacheService(), new WorkflowNavigator());
        Assert.True(vm.ContinueCommand.CanExecute(null));

        vm.Clips[0].IsIncluded = false;

        Assert.False(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void ContinueCommand_Execute_BuildsReviewedClipsFromIncludedValidRowsAndNavigates()
    {
        var session = BuildSessionWithClips(out var accepted, out var rejected);
        var navigator = new WorkflowNavigator();
        var vm = new SceneReviewViewModel(session, new FakeThumbnailCacheService(), navigator);
        vm.Clips[1].IsIncluded = true;
        vm.Clips[1].AdjustedStart = rejected.Range.Start + TimeSpan.FromSeconds(1);

        vm.ContinueCommand.Execute(null);

        Assert.NotNull(session.ReviewedClips);
        Assert.Equal(2, session.ReviewedClips!.Count);
        var reviewedRejected = session.ReviewedClips.Single(c => c.Range.Start == rejected.Range.Start + TimeSpan.FromSeconds(1));
        Assert.Equal(rejected.Range.End, reviewedRejected.Range.End);
        Assert.Equal(WorkflowStep.TimelineSummary, navigator.CurrentStep);
    }

    [Fact]
    public void Constructor_RevisitingAfterPriorEdits_RestoresOverridesFromSession()
    {
        var session = BuildSessionWithClips(out var accepted, out _);
        var firstVisit = new SceneReviewViewModel(session, new FakeThumbnailCacheService(), new WorkflowNavigator());
        firstVisit.Clips[0].IsIncluded = false;

        var secondVisit = new SceneReviewViewModel(session, new FakeThumbnailCacheService(), new WorkflowNavigator());

        Assert.False(secondVisit.Clips[0].IsIncluded);
    }

    private static WorkflowSession BuildSessionWithClips(out CleanClip accepted, out CleanClip rejected)
    {
        accepted = CleanClipBuilder.Build(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), accepted: true);
        rejected = CleanClipBuilder.Build(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(13), accepted: false);

        return new WorkflowSession
        {
            VideoFilePath = "video.mp4",
            ExtractionResult = new CleanClipExtractionResult
            {
                RemainingCleanRanges = [],
                AcceptedClips = [accepted],
                RejectedClips = [rejected],
                Clusters = [],
            },
        };
    }
}
