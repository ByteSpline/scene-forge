namespace SceneForge.Accuracy.Fixtures;

// One generated clip plus its ground truth. Expected is empty for
// distractor groups (BlackHold/FrozenFrame/StaticShot/RapidMotion) - any
// detection reported against such a fixture is a false positive by
// construction, never a match.
public sealed record SyntheticFixture(
    string Id,
    FixtureGroup Group,
    string FilePath,
    double SourceDurationSeconds,
    IReadOnlyList<ExpectedTransition> Expected);
