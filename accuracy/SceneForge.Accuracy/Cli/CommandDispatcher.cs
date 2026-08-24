namespace SceneForge.Accuracy.Cli;

// Deliberately no CLI-parsing library (matches the repo's existing
// zero-CLI-dependency convention - see benchmarks/SceneForge.Benchmarks's
// one-line Program.cs, which likewise delegates entirely to a switcher
// rather than a bespoke parser).
public static class CommandDispatcher
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0];
        var options = CommandLineOptions.Parse(args.Skip(1).ToArray());

        try
        {
            return command switch
            {
                "generate" => await GenerateCommand.RunAsync(options, CancellationToken.None).ConfigureAwait(false),
                "evaluate" => await EvaluateCommand.RunAsync(options, CancellationToken.None).ConfigureAwait(false),
                "gate" => await GateCommand.RunAsync(options, CancellationToken.None).ConfigureAwait(false),
                "update-baseline" => await UpdateBaselineCommand.RunAsync(options, CancellationToken.None).ConfigureAwait(false),
                _ => Unknown(command),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"'{command}' failed: {ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: SceneForge.Accuracy <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  generate         --output <dir> --manifest <path> [--ffmpeg-base-dir <dir>]");
        Console.WriteLine("  evaluate         [--profile Fast|Balanced|Accurate] [--report <json>] [--ffmpeg-base-dir <dir>]");
        Console.WriteLine("  gate             --baseline <path> [...evaluate options]");
        Console.WriteLine("  update-baseline  --output <path> [...evaluate options]");
    }
}
