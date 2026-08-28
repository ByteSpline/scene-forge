namespace SceneForge.Media.Rendering;

// Priority order IHardwareEncoderProbe tests candidates in - NVENC first,
// then Intel Quick Sync, then AMD AMF, then the software fallbacks
// (libx264, then libopenh264 for ffmpeg builds compiled --disable-libx264).
// Declared in this order deliberately so HardwareEncoderProbe.Candidates can
// express "try these in order" as a simple array literal rather than a
// separate ranking table.
public enum VideoEncoderKind
{
    NvidiaNvenc,
    IntelQuickSync,
    AmdAmf,
    SoftwareX264,
    SoftwareOpenH264,
}
