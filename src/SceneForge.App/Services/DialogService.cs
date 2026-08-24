using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace SceneForge.App.Services;

public sealed class DialogService : IDialogService
{
    private const string VideoFilter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.m4v;*.webm|All files|*.*";
    private const string AudioFilter = "Audio files|*.mp3;*.wav;*.aac;*.m4a;*.flac;*.ogg|All files|*.*";

    public string? ShowOpenVideoFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a source video",
            Filter = VideoFilter,
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowOpenAudioFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a background audio track",
            Filter = AudioFilter,
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSaveVideoFileDialog(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Choose where to save the rendered video",
            Filter = "MP4 video|*.mp4",
            DefaultExt = ".mp4",
            AddExtension = true,
            FileName = suggestedFileName,
            OverwritePrompt = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowError(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool ShowConfirmation(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void RevealInFileExplorer(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
        {
            UseShellExecute = true,
        });
    }
}
