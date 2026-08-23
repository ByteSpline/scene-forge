using System.Buffers;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Tests.TestSupport;

// Builds hand-crafted FrameSample instances with known pixel content, for
// deterministic known-answer tests of Detection signal extractors and
// classifiers - no ffmpeg involved.
internal static class FrameSampleBuilder
{
    public static FrameSample SolidColor(
        byte blue,
        byte green,
        byte red,
        int width = 16,
        int height = 16,
        int frameIndex = 0,
        TimeSpan? timestamp = null)
    {
        var buffer = new byte[width * height * 3];
        for (var i = 0; i < buffer.Length; i += 3)
        {
            buffer[i] = blue;
            buffer[i + 1] = green;
            buffer[i + 2] = red;
        }

        return FromBgrBytes(buffer, width, height, frameIndex, timestamp ?? TimeSpan.Zero);
    }

    // Alternating black/white squares - guarantees a non-zero edge density
    // and Laplacian variance, unlike any solid color.
    public static FrameSample Checkerboard(
        int width = 16,
        int height = 16,
        int squareSize = 2,
        int frameIndex = 0,
        TimeSpan? timestamp = null)
    {
        var buffer = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var isLight = ((x / squareSize) + (y / squareSize)) % 2 == 0;
                byte value = isLight ? (byte)255 : (byte)0;
                var offset = ((y * width) + x) * 3;
                buffer[offset] = value;
                buffer[offset + 1] = value;
                buffer[offset + 2] = value;
            }
        }

        return FromBgrBytes(buffer, width, height, frameIndex, timestamp ?? TimeSpan.Zero);
    }

    // A continuous 2D sinusoidal texture (rich local gradients in both x and
    // y, no hard edges/discontinuities) translated by (shiftX, shiftY) -
    // deliberately not a flat gradient or a periodic checkerboard, both of
    // which are known pathological/ambiguous cases for gradient-based dense
    // optical flow (the aperture problem, or aliasing against the pattern's
    // own period). Gives GlobalMotionEstimateExtractor tests a frame pair
    // with known, controllable, reliably-recoverable apparent motion.
    public static FrameSample TexturedShift(
        int width = 64,
        int height = 64,
        double shiftX = 0,
        double shiftY = 0,
        int frameIndex = 0,
        TimeSpan? timestamp = null)
    {
        var buffer = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sourceX = x - shiftX;
                var sourceY = y - shiftY;
                var wave = (Math.Sin(sourceX * 0.35) * Math.Cos(sourceY * 0.35))
                    + (Math.Sin((sourceX + sourceY) * 0.17) * 0.5);
                var value = (byte)Math.Clamp(127.5 + (wave * 100), 0, 255);
                var offset = ((y * width) + x) * 3;
                buffer[offset] = value;
                buffer[offset + 1] = value;
                buffer[offset + 2] = value;
            }
        }

        return FromBgrBytes(buffer, width, height, frameIndex, timestamp ?? TimeSpan.Zero);
    }

    public static FrameSample FromBgrBytes(byte[] bgrBytes, int width, int height, int frameIndex, TimeSpan timestamp)
    {
        var pool = ArrayPool<byte>.Shared;
        var rented = pool.Rent(bgrBytes.Length);
        Array.Copy(bgrBytes, rented, bgrBytes.Length);

        return new FrameSample(
            rented,
            bgrBytes.Length,
            frameIndex,
            timestamp,
            width,
            height,
            FrameSamplePixelFormat.Bgr24,
            pool);
    }

    public static FrameSample Gray8SolidColor(byte value, int width = 16, int height = 16)
    {
        var pool = ArrayPool<byte>.Shared;
        var buffer = new byte[width * height];
        Array.Fill(buffer, value);
        var rented = pool.Rent(buffer.Length);
        Array.Copy(buffer, rented, buffer.Length);

        return new FrameSample(
            rented,
            buffer.Length,
            0,
            TimeSpan.Zero,
            width,
            height,
            FrameSamplePixelFormat.Gray8,
            pool);
    }
}
