namespace SceneForge.Accuracy;

public static class Program
{
    public static Task<int> Main(string[] args) => Cli.CommandDispatcher.RunAsync(args);
}
