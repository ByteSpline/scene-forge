namespace SceneForge.Media.Rendering;

// The result of IHardwareEncoderProbe capability testing - never chosen from
// a GPU vendor-name/model lookup table (the phase brief's explicit
// requirement), only from an encoder that has just been proven, this run,
// to actually launch and produce output on this machine's ffmpeg build.
public sealed record VideoEncoderSelection
{
    public required VideoEncoderKind Kind { get; init; }

    // The exact ffmpeg -c:v value, e.g. "h264_nvenc", "h264_qsv", "h264_amf", "libx264".
    public required string FfmpegEncoderName { get; init; }

    public required bool IsHardwareAccelerated { get; init; }
}
