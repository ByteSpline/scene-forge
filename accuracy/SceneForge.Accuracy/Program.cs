using SceneForge.Accuracy.Cli;

namespace SceneForge.Accuracy;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Cooperative shutdown (CLAUDE.md rule 5): a fixture-matrix run
        // takes a minute or more (real ffmpeg encodes + real detector
        // analysis over ~30 fixtures), so Ctrl+C must flow all the way
        // down to ProcessRunner - which kills the whole ffmpeg process
        // tree on cancellation - rather than leaving the default .NET
        // behavior of abruptly terminating this process and orphaning
        // whichever ffmpeg.exe was mid-encode.
        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        return await CommandDispatcher.RunAsync(args, cancellationSource.Token).ConfigureAwait(false);
    }
}
