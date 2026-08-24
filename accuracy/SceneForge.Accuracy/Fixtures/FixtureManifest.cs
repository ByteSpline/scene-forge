namespace SceneForge.Accuracy.Fixtures;

public sealed record FixtureManifest(IReadOnlyList<FixtureManifestEntry> Entries)
{
    public static FixtureManifest FromFixtures(IReadOnlyList<SyntheticFixture> fixtures) =>
        new(fixtures.Select(FixtureManifestEntry.FromFixture).ToList());
}
