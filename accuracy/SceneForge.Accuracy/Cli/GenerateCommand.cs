using SceneForge.Accuracy.Fixtures;

namespace SceneForge.Accuracy.Cli;

// The "regenerate the committed ground truth" path: builds every fixture in
// SyntheticFixtureCatalog.BuildAllAsync into --output (not committed - see
// tests/fixtures/README.md) and (re)writes the compact manifest JSON that
// IS committed to tests/fixtures/manifest.json.
public static class GenerateCommand
{
    public static async Task<int> RunAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var outputDirectory = options.Require("output");
        var manifestPath = options.Require("manifest");
        var applicationBaseDirectory = options.GetOrDefault("ffmpeg-base-dir") ?? AppContext.BaseDirectory;
        var ffmpegPath = Path.Combine(applicationBaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");

        if (!File.Exists(ffmpegPath))
        {
            Console.Error.WriteLine($"ffmpeg.exe was not found at '{ffmpegPath}'. Pass --ffmpeg-base-dir <dir> pointing at a directory containing tools/ffmpeg/ffmpeg.exe.");
            return 1;
        }

        var catalog = new SyntheticFixtureCatalog(ffmpegPath, outputDirectory);
        var fixtures = await catalog.BuildAllAsync(cancellationToken).ConfigureAwait(false);

        var manifest = FixtureManifest.FromFixtures(fixtures);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!);
        await FixtureManifestJson.WriteAsync(manifest, manifestPath, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Built {fixtures.Count} fixtures into '{outputDirectory}'.");
        Console.WriteLine($"Wrote compact ground truth for {manifest.Entries.Count} fixtures to '{manifestPath}'.");
        return 0;
    }
}
