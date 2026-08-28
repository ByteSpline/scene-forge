namespace SceneForge.Media.Rendering.Internal;

// Conservative, documented-heuristic quality/rate-control arguments per
// encoder - not tuned or benchmarked against measured quality/size targets
// (CLAUDE.md rule 9 applies to optimizations; no baseline exists yet for
// encoder-quality tuning, see docs/PHASE_09_REPORT.md Outstanding). Shared
// by the real render passes (FFmpegRenderService) and the encoder
// capability probe (HardwareEncoderProbe) so the probe exercises the exact
// settings a real render will use.
internal static class EncoderQualityDefaults
{
    public static IReadOnlyList<string> For(VideoEncoderKind kind) => kind switch
    {
        VideoEncoderKind.NvidiaNvenc => ["-preset", "p4", "-rc", "vbr", "-cq", "20"],
        VideoEncoderKind.IntelQuickSync => ["-preset", "medium", "-global_quality", "20"],
        VideoEncoderKind.AmdAmf => ["-quality", "balanced", "-rc", "cqp", "-qp_i", "20", "-qp_p", "20"],
        // libopenh264 has no CRF mode; a mid constant bitrate is the safe
        // last-resort default (this path runs only when no hardware encoder
        // and no libx264 are available).
        VideoEncoderKind.SoftwareOpenH264 => ["-b:v", "5M"],
        _ => ["-preset", "medium", "-crf", "20"],
    };
}
