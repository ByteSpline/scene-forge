using System.Text.Json;
using SceneForge.Accuracy.Json;

namespace SceneForge.Accuracy.Fixtures;

// Write-only: the manifest is a human-reviewable, git-diffable artifact of
// what `generate` built (committed to tests/fixtures/manifest.json), not
// something the tool reads back - `evaluate`/`gate` always rebuild the
// matrix fresh from SyntheticFixtureCatalog, the single source of truth for
// ground truth (see the catalog's own remarks).
public static class FixtureManifestJson
{
    public static async Task WriteAsync(FixtureManifest manifest, string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, manifest, AccuracyJsonOptions.Options, cancellationToken).ConfigureAwait(false);
    }
}
