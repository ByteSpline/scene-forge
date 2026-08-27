using SceneForge.Media.Tooling;

namespace SceneForge.Media.Tests.TestSupport;

internal sealed class FakeNativeLibraryProbe : INativeLibraryProbe
{
    private readonly HashSet<string> _unloadableLibraries;

    public FakeNativeLibraryProbe(params string[] unloadableLibraries)
    {
        _unloadableLibraries = new HashSet<string>(unloadableLibraries, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsLoadable(string libraryFileName) => !_unloadableLibraries.Contains(libraryFileName);
}
