using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace SceneForge.Media.Tooling;

// Packaging bundles OpenCvSharpExtern.dll under 'tools\opencv' next to the
// executable, never PATH (same rule FfmpegToolLocator applies to
// ffmpeg/ffprobe - see its remarks) and never directly beside the exe: the
// default native-library search a packaged single-file publish gets only
// covers AppContext.BaseDirectory itself, not an arbitrary subfolder.
// OpenCvSharp4's [DllImport("OpenCvSharpExtern")] declarations live in the
// OpenCvSharp4 assembly (OpenCvSharp.Mat etc.), not in SceneForge.Media, so
// the resolver has to be registered against THAT assembly - registering it
// against SceneForge.Media's own assembly would never be consulted.
//
// [ModuleInitializer] guarantees this registration runs before any type in
// SceneForge.Media is used, which is early enough: every OpenCvSharp call
// in this codebase happens from inside SceneForge.Media (see
// Detection/Extraction/Signals and OpenCvNativeProbe), so none of them can
// run before this module's initializer has already executed.
internal static class OpenCvNativeLibraryResolver
{
    private const string LibraryName = "OpenCvSharpExtern";

    // CA2255 assumes ModuleInitializer belongs only in an application entry
    // assembly; this library is only ever loaded BY an application (or a
    // test/tool host), and the resolver must be registered before the
    // first OpenCvSharp P/Invoke regardless of which one loads it - a
    // module initializer is the only mechanism that guarantees that for
    // every possible host, so the rule does not apply to this case.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        NativeLibrary.SetDllImportResolver(typeof(Mat).Assembly, Resolve);
    }
#pragma warning restore CA2255

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return nint.Zero;
        }

        // Falls through to IntPtr.Zero (default probing) when the packaged
        // layout doesn't exist - exactly the case for a normal dev/test
        // build, where OpenCvSharp4.runtime.win's own build target already
        // copies the DLL to 'runtimes\win-x64\native\' next to the test
        // host, and default resolution finds it there unchanged.
        var packagedPath = Path.Combine(AppContext.BaseDirectory, "tools", "opencv", LibraryName + ".dll");
        return File.Exists(packagedPath) && NativeLibrary.TryLoad(packagedPath, out var handle)
            ? handle
            : nint.Zero;
    }
}
