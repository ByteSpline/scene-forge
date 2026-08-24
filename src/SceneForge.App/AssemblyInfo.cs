using System.Runtime.CompilerServices;

// Lets SceneForge.App.Tests exercise StartupRecoveryRunner's internal
// per-source restore logic directly against fakes, rather than only through
// a real DI container/real dialogs/real ffprobe - see
// StartupRecoveryRunnerTests.
[assembly: InternalsVisibleTo("SceneForge.App.Tests")]
