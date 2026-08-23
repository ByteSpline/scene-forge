namespace SceneForge.Media.Rendering;

public interface IRenderPlanBuilder
{
    // Pure and synchronous - no I/O beyond the cheap path-existence checks
    // MediaPathValidator already performs elsewhere in this codebase, no
    // ffmpeg/ffprobe process spawned. See RenderPlanBuilder.
    RenderPlan Build(RenderPlanRequest request);
}
