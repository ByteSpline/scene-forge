using SceneForge.Media.Processes;

namespace SceneForge.Media.Tests.Processes;

// Proves the no-shell-interpolation hardening property at the ProcessStartInfo
// construction level (via InternalsVisibleTo), which is deterministic and
// fast. An end-to-end test that tries to smuggle shell metacharacters through
// a real child process would itself have to launch a shell to observe the
// result, which would test the shell's parsing rather than ours.
public class ProcessRunnerBuildStartInfoTests
{
    [Fact]
    public void BuildStartInfo_NeverUsesShellExecute()
    {
        var startInfo = ProcessRunner.BuildStartInfo(new ProcessExecutionRequest { FileName = "ffprobe.exe", Arguments = ["-version"] });

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.RedirectStandardInput);
    }

    [Theory]
    [InlineData("& echo hacked")]
    [InlineData("path with spaces\\file.mp4")]
    [InlineData("\"already-quoted\"")]
    [InlineData("a;b|c&d")]
    [InlineData("$(rm -rf /)")]
    public void BuildStartInfo_ArgumentsWithShellMetacharacters_PreservedAsDiscreteArgvEntries(string dangerousArgument)
    {
        var startInfo = ProcessRunner.BuildStartInfo(new ProcessExecutionRequest
        {
            FileName = "ffprobe.exe",
            Arguments = ["-i", dangerousArgument],
        });

        Assert.Equal(["-i", dangerousArgument], startInfo.ArgumentList);
        // The legacy single-string Arguments property is never populated - if it
        // were, .NET would re-parse it as command-line text instead of passing
        // ArgumentList entries through as literal argv elements.
        Assert.Equal(string.Empty, startInfo.Arguments);
    }

    [Fact]
    public void BuildStartInfo_NoWorkingDirectorySpecified_LeavesItUnset()
    {
        var startInfo = ProcessRunner.BuildStartInfo(new ProcessExecutionRequest { FileName = "ffprobe.exe", Arguments = [] });

        Assert.Equal(string.Empty, startInfo.WorkingDirectory);
    }

    [Fact]
    public void BuildStartInfo_WorkingDirectorySpecified_IsPassedThrough()
    {
        var startInfo = ProcessRunner.BuildStartInfo(new ProcessExecutionRequest
        {
            FileName = "ffprobe.exe",
            Arguments = [],
            WorkingDirectory = @"C:\media",
        });

        Assert.Equal(@"C:\media", startInfo.WorkingDirectory);
    }
}
