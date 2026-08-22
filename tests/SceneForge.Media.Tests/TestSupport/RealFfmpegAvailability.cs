namespace SceneForge.Media.Tests.TestSupport;

// Optional integration tests probe for real ffprobe.exe/ffmpeg.exe under
// tools/ffmpeg next to the test binaries (the same relative layout
// FfmpegToolLocator expects next to the shipped app) and skip themselves
// when the binaries are absent, since they are never committed to the repo.
internal static class RealFfmpegAvailability
{
    public static bool IsAvailable => File.Exists(FfprobePath) && File.Exists(FfmpegPath);

    public static string ToolsDirectory => Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");

    public static string FfprobePath => Path.Combine(ToolsDirectory, "ffprobe.exe");

    public static string FfmpegPath => Path.Combine(ToolsDirectory, "ffmpeg.exe");

    public static string SkipReason =>
        $"Real ffmpeg/ffprobe binaries were not found at '{ToolsDirectory}'. " +
        "Copy ffprobe.exe, ffmpeg.exe, and their dependent DLLs there to run this integration test locally; it is always skipped in CI.";
}
