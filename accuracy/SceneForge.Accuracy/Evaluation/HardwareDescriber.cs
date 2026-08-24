using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SceneForge.Accuracy.Evaluation;

// Best-effort: WMI's Win32_Processor name lookup is the only piece that can
// fail (e.g. a locked-down environment), and a baseline missing just the
// CPU's marketing name is still far more useful than failing the whole
// `update-baseline` run over it.
public static class HardwareDescriber
{
    public static HardwareDescription Describe()
    {
        var cpuName = TryGetCpuName() ?? "Unknown";
        var totalMemoryGigabytes = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0, 1);

        return new HardwareDescription(
            cpuName,
            Environment.ProcessorCount,
            totalMemoryGigabytes,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription);
    }

    private static string? TryGetCpuName()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"(Get-CimInstance Win32_Processor).Name\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }
}
