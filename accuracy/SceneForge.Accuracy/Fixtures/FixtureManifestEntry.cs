using SceneForge.Media.Domain;

namespace SceneForge.Accuracy.Fixtures;

// The *compact*, committed ground truth for one fixture: everything needed
// to know what SyntheticFixtureCatalog should have built and what a
// detector run over it should find, without the machine-local file path
// (ephemeral - fixtures are regenerated fresh, never committed as binaries)
// or a redundant transition Type per entry (every entry in a fixture shares
// the fixture's own Group by construction - see SyntheticFixtureCatalog's
// remarks).
public sealed record FixtureManifestEntry(
    string Id,
    FixtureGroup Group,
    double SourceDurationSeconds,
    IReadOnlyList<TimeRange> ExpectedWindows)
{
    public static FixtureManifestEntry FromFixture(SyntheticFixture fixture) => new(
        fixture.Id,
        fixture.Group,
        fixture.SourceDurationSeconds,
        fixture.Expected.Select(e => e.Window).ToList());
}
