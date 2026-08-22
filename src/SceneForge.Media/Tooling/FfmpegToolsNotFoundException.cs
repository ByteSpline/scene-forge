namespace SceneForge.Media.Tooling;

public sealed class FfmpegToolsNotFoundException : Exception
{
    public IReadOnlyList<string> MissingPaths { get; }

    public FfmpegToolsNotFoundException(IReadOnlyList<string> missingPaths)
        : base(BuildMessage(missingPaths))
    {
        MissingPaths = missingPaths;
    }

    private static string BuildMessage(IReadOnlyList<string> missingPaths) =>
        "Required FFmpeg tooling was not found next to the application: "
        + string.Join(", ", missingPaths)
        + ". SceneForge never searches the system PATH for these binaries in a packaged"
        + " build; place ffmpeg.exe and ffprobe.exe under a 'tools\\ffmpeg' folder"
        + " next to the application executable.";
}
