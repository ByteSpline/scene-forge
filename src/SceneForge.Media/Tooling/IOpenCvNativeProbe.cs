namespace SceneForge.Media.Tooling;

public interface IOpenCvNativeProbe
{
    // Makes a real call into the OpenCV native library and returns a short
    // diagnostic string on success. Throws whatever the underlying P/Invoke
    // failure actually is (DllNotFoundException, BadImageFormatException,
    // EntryPointNotFoundException, etc.) on failure - callers decide how to
    // present that, this only proves whether the call itself worked.
    string Probe();
}
