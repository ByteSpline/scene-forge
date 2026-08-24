namespace SceneForge.Accuracy.Cli;

// Minimal "--name value" parser - the only shape this tool's commands need.
// A bare flag with no following value is rejected rather than silently
// treated as present/absent, so a typo'd option fails loudly instead of
// being misread as the next option's value.
public sealed class CommandLineOptions
{
    private readonly Dictionary<string, string> _values;

    private CommandLineOptions(Dictionary<string, string> values)
    {
        _values = values;
    }

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Expected an option starting with '--', got '{arg}'.");
            }

            if (i + 1 >= args.Count)
            {
                throw new ArgumentException($"Option '{arg}' requires a value.");
            }

            values[arg[2..]] = args[++i];
        }

        return new CommandLineOptions(values);
    }

    public string? GetOrDefault(string name) => _values.GetValueOrDefault(name);

    public string Require(string name) =>
        _values.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"Missing required option '--{name}'.");
}
