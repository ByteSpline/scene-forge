namespace SceneForge.App.Services;

// Every native-dialog/shell interaction a ViewModel needs, behind an
// interface so ViewModels stay unit-testable without a real Win32 dialog
// (CLAUDE.md rule 4/8: no UI-framework dependency inside test-covered
// logic). The real implementation (DialogService) wraps
// Microsoft.Win32.OpenFileDialog/SaveFileDialog and System.Windows.MessageBox
// - both local, synchronous, offline Win32 APIs, never a network call
// (CLAUDE.md rule 2).
public interface IDialogService
{
    string? ShowOpenVideoFileDialog();

    string? ShowOpenAudioFileDialog();

    string? ShowSaveVideoFileDialog(string suggestedFileName);

    void ShowError(string title, string message);

    bool ShowConfirmation(string title, string message);

    void RevealInFileExplorer(string filePath);
}
