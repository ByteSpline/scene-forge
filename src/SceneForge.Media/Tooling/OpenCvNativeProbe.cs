using OpenCvSharp;

namespace SceneForge.Media.Tooling;

public sealed class OpenCvNativeProbe : IOpenCvNativeProbe
{
    // Cv2.GetBuildInformation() is a cheap, side-effect-free native call
    // that exercises the same OpenCvSharpExtern.dll P/Invoke path every
    // real signal extractor uses (see Detection/Extraction/Signals), and
    // its return value doubles as useful diagnostic detail (OpenCV version,
    // build flags) rather than a bare "it worked".
    public string Probe() => Cv2.GetBuildInformation();
}
