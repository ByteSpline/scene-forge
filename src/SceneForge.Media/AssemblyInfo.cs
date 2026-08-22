using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SceneForge.Media.Tests")]

// Lets SceneForge.Benchmarks exercise FrameSampler through its internal
// synthetic-process seam (SceneForge.Media.Sampling.IFrameSamplingProcess
// et al.), so throughput/allocation benchmarks measure the real pipeline
// without spawning a real ffmpeg process per iteration.
[assembly: InternalsVisibleTo("SceneForge.Benchmarks")]
