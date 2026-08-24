namespace SceneForge.Accuracy.Cli;

// Deliberately no CLI-parsing library (matches the repo's existing
// zero-CLI-dependency convention - see benchmarks/SceneForge.Benchmarks's
// one-line Program.cs, which likewise delegates entirely to a switcher
// rather than a bespoke parser). Option parsing happens *inside* the try
// block, not before it: a malformed invocation (a dangling "--flag" with
// no value, a missing required option) must produce the same clean,
// non-zero-exit error path as a command that fails at runtime, never an
// unhandled-exception crash dump.
public static class CommandDispatcher
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return ExitCodeMapper.FailureExitCode;
        }

        var command = args[0];

        try
        {
            var options = CommandLineOptions.Parse(args.Skip(1).ToArray());

            return command switch
            {
                "generate" => await GenerateCommand.RunAsync(options, cancellationToken).ConfigureAwait(false),
                "evaluate" => await EvaluateCommand.RunAsync(options, cancellationToken).ConfigureAwait(false),
                "gate" => await GateCommand.RunAsync(options, cancellationToken).ConfigureAwait(false),
                "update-baseline" => await UpdateBaselineCommand.RunAsync(options, cancellationToken).ConfigureAwait(false),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            return ExitCodeMapper.Map(ex, command, Console.Error);
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return ExitCodeMapper.FailureExitCode;
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
