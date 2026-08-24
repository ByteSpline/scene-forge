using System.IO;
using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;

namespace SceneForge.App.Tests.ViewModels;

public sealed class WelcomeImportViewModelTests : IDisposable
{
    private readonly string _videoPath;
    private readonly string _audioPath;
    private readonly string _videoOnlyMediaPath;

    public WelcomeImportViewModelTests()
    {
        _videoPath = CreateTempFile();
        _audioPath = CreateTempFile();
        _videoOnlyMediaPath = CreateTempFile();
    }

    public void Dispose()
    {
        TryDelete(_videoPath);
        TryDelete(_audioPath);
        TryDelete(_videoOnlyMediaPath);
    }

    [Fact]
    public async Task VideoImportCommand_ValidVideoFile_UpdatesSummaryAndSession()
    {
        var session = new WorkflowSession();
        var ffprobe = new FakeFfprobeService();
        ffprobe.ResultsByPath[_videoPath] = MediaInfoBuilder.Video(_videoPath, TimeSpan.FromSeconds(30));
        var vm = new WelcomeImportViewModel(session, new FakeDialogService(), ffprobe, new WorkflowNavigator());

        await vm.VideoImportCommand.ExecuteAsync(_videoPath);

        Assert.Equal(_videoPath, session.VideoFilePath);
        Assert.NotNull(session.VideoMediaInfo);
        Assert.Contains("0:30", vm.VideoSummary);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task VideoImportCommand_FileWithNoVideoStream_SetsErrorMessageAndLeavesSessionUnset()
    {
        var session = new WorkflowSession();
        var ffprobe = new FakeFfprobeService();
        ffprobe.ResultsByPath[_videoOnlyMediaPath] = MediaInfoBuilder.Audio(_videoOnlyMediaPath, TimeSpan.FromSeconds(10));
        var vm = new WelcomeImportViewModel(session, new FakeDialogService(), ffprobe, new WorkflowNavigator());

        await vm.VideoImportCommand.ExecuteAsync(_videoOnlyMediaPath);

        Assert.Null(session.VideoMediaInfo);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task ContinueCommand_CanExecute_OnlyAfterBothVideoAndAudioImported()
    {
        var session = new WorkflowSession();
        var ffprobe = new FakeFfprobeService();
        ffprobe.ResultsByPath[_videoPath] = MediaInfoBuilder.Video(_videoPath, TimeSpan.FromSeconds(30));
        ffprobe.ResultsByPath[_audioPath] = MediaInfoBuilder.Audio(_audioPath, TimeSpan.FromSeconds(30));
        var vm = new WelcomeImportViewModel(session, new FakeDialogService(), ffprobe, new WorkflowNavigator());

        Assert.False(vm.ContinueCommand.CanExecute(null));

        await vm.VideoImportCommand.ExecuteAsync(_videoPath);
        Assert.False(vm.ContinueCommand.CanExecute(null));

        await vm.AudioImportCommand.ExecuteAsync(_audioPath);
        Assert.True(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public async Task ContinueCommand_Execute_NavigatesToAnalysisSettings()
    {
        var session = new WorkflowSession();
        var ffprobe = new FakeFfprobeService();
        ffprobe.ResultsByPath[_videoPath] = MediaInfoBuilder.Video(_videoPath, TimeSpan.FromSeconds(30));
        ffprobe.ResultsByPath[_audioPath] = MediaInfoBuilder.Audio(_audioPath, TimeSpan.FromSeconds(30));
        var navigator = new WorkflowNavigator();
        var vm = new WelcomeImportViewModel(session, new FakeDialogService(), ffprobe, navigator);
        await vm.VideoImportCommand.ExecuteAsync(_videoPath);
        await vm.AudioImportCommand.ExecuteAsync(_audioPath);

        vm.ContinueCommand.Execute(null);

        Assert.Equal(WorkflowStep.AnalysisSettings, navigator.CurrentStep);
    }

    [Fact]
    public async Task BrowseVideoCommand_DialogReturnsNull_DoesNotProbe()
    {
        var session = new WorkflowSession();
        var dialogService = new FakeDialogService { VideoPathToReturn = null };
        var vm = new WelcomeImportViewModel(session, dialogService, new FakeFfprobeService(), new WorkflowNavigator());

        await vm.BrowseVideoCommand.ExecuteAsync(null);

        Assert.Null(session.VideoMediaInfo);
        Assert.Null(vm.VideoFilePath);
    }

    [Fact]
    public void Constructor_SessionAlreadyHasImportedFiles_PrefillsSummaries()
    {
        var session = new WorkflowSession
        {
            VideoFilePath = _videoPath,
            VideoMediaInfo = MediaInfoBuilder.Video(_videoPath, TimeSpan.FromSeconds(12)),
        };

        var vm = new WelcomeImportViewModel(session, new FakeDialogService(), new FakeFfprobeService(), new WorkflowNavigator());

        Assert.Equal(_videoPath, vm.VideoFilePath);
        Assert.NotNull(vm.VideoSummary);
    }

    private static string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sceneforge-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
