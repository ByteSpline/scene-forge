using SceneForge.App.Services;

namespace SceneForge.App.Tests.TestSupport;

internal sealed class FakeDialogService : IDialogService
{
    public string? VideoPathToReturn { get; set; }

    public string? AudioPathToReturn { get; set; }

    public string? SavePathToReturn { get; set; }

    public bool ConfirmationResult { get; set; } = true;

    public List<(string Title, string Message)> Errors { get; } = [];

    public List<string> RevealedPaths { get; } = [];

    public string? ShowOpenVideoFileDialog() => VideoPathToReturn;

    public string? ShowOpenAudioFileDialog() => AudioPathToReturn;

    public string? ShowSaveVideoFileDialog(string suggestedFileName) => SavePathToReturn;

    public void ShowError(string title, string message) => Errors.Add((title, message));

    public bool ShowConfirmation(string title, string message) => ConfirmationResult;

    public void RevealInFileExplorer(string filePath) => RevealedPaths.Add(filePath);
}
