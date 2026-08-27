using System.Runtime.InteropServices;

namespace SceneForge.Media.Tooling;

// Checks whether a named native library resolves through the OS's normal
// system-component search order (WinSxS/System32/app directory) -
// deliberately NOT the app's own PATH-free 'tools' convention that
// FfmpegToolLocator and OpenCvNativeLibraryResolver use for app-bundled
// tooling. The Visual C++ runtime is an OS-managed, System32-resident
// component installed by Microsoft's own redistributable, not something
// SceneForge bundles or searches PATH for, so using the standard system
// search order here does not conflict with CLAUDE.md's "never through
// PATH" rule for ffmpeg/OpenCV tooling.
public sealed class SystemNativeLibraryProbe : INativeLibraryProbe
{
    public bool IsLoadable(string libraryFileName)
    {
        if (!NativeLibrary.TryLoad(libraryFileName, out var handle))
        {
            return false;
        }

        NativeLibrary.Free(handle);
        return true;
    }
}
