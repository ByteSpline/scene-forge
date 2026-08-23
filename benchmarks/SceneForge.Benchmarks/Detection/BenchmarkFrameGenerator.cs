using System.Buffers;
using SceneForge.Media.Sampling;

namespace SceneForge.Benchmarks.Detection;

// Generates a deterministic sequence of synthetic in-memory FrameSamples -
// no real ffmpeg process, no real video file - so the Detection benchmarks
// measure the signal-extraction + classification pipeline's own cost, not
// decode/process I/O (ffmpeg's cost, not this code's), matching
// FrameSamplingBenchmarks' approach for the Sampling pipeline. Content
// varies frame-to-frame (a moving diagonal pattern, with an occasional
// simulated hard cut) so every signal extractor - including motion - has
// genuine, non-degenerate work to do rather than measuring the all-zero
// fast path.
internal static class BenchmarkFrameGenerator
{
    public static IEnumerable<FrameSample> Generate(int width, int height, int totalFrames)
    {
        for (var frameIndex = 0; frameIndex < totalFrames; frameIndex++)
        {
            yield return CreateFrame(width, height, frameIndex);
        }
    }

    private static FrameSample CreateFrame(int width, int height, int frameIndex)
    {
        // Every 50th frame is a hard cut to a different phase/palette so
        // HardCutClassifier and DissolveClassifier both have real
        // (non-empty) candidates to evaluate during the run, not just
        // GlobalMotionEstimateExtractor's cost.
        var cutPhase = frameIndex / 50;
        var t = frameIndex % 50;

        var buffer = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var wave = Math.Sin(((x + t * 2) * 0.05) + (cutPhase * 1.7)) * Math.Cos((y * 0.05) + (cutPhase * 0.9));
                var value = (byte)Math.Clamp(127.5 + (wave * 110), 0, 255);
                var offset = ((y * width) + x) * 3;
                buffer[offset] = value;
                buffer[offset + 1] = (byte)((value + (cutPhase * 40)) % 256);
                buffer[offset + 2] = (byte)((255 - value + (cutPhase * 20)) % 256);
            }
        }

        var pool = ArrayPool<byte>.Shared;
        var rented = pool.Rent(buffer.Length);
        Array.Copy(buffer, rented, buffer.Length);

        return new FrameSample(
            rented,
            buffer.Length,
            frameIndex,
            TimeSpan.FromSeconds(frameIndex * 0.125),
            width,
            height,
            FrameSamplePixelFormat.Bgr24,
            pool);
    }
}
