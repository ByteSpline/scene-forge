using SceneForge.Media.Processes;

namespace SceneForge.Media.Tests.Processes;

public class ProcessExecutionRequestTests
{
    [Fact]
    public void MaxCapturedBytesPerStream_DefaultsToFourMegabytes()
    {
        var request = new ProcessExecutionRequest { FileName = "ffprobe.exe", Arguments = [] };

        Assert.Equal(4 * 1024 * 1024, request.MaxCapturedBytesPerStream);
    }

    [Fact]
    public void MaxCapturedBytesPerStream_BelowFloor_IsClampedUp()
    {
        var request = new ProcessExecutionRequest { FileName = "ffprobe.exe", Arguments = [], MaxCapturedBytesPerStream = 1 };

        Assert.Equal(1024, request.MaxCapturedBytesPerStream);
    }

    [Fact]
    public void MaxCapturedBytesPerStream_AboveCeiling_IsClampedDown()
    {
        var request = new ProcessExecutionRequest { FileName = "ffprobe.exe", Arguments = [], MaxCapturedBytesPerStream = int.MaxValue };

        Assert.Equal(64 * 1024 * 1024, request.MaxCapturedBytesPerStream);
    }
}
