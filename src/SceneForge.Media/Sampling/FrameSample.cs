using System.Buffers;

namespace SceneForge.Media.Sampling;

// One decoded, downscaled frame. Buffer is rented from an ArrayPool and MUST
// be released by disposing this instance once the consumer is done with it
// (CLAUDE.md rule 7: no unbounded retention) - typically via `await using`
// or `using` in an `await foreach` loop. Buffer.Length may be larger than
// ByteLength; only the first ByteLength bytes are valid frame data.
public sealed class FrameSample : IDisposable
{
    private readonly ArrayPool<byte> _pool;
    private int _disposed;

    internal FrameSample(
        byte[] buffer,
        int byteLength,
        int frameIndex,
        TimeSpan timestamp,
        int width,
        int height,
        FrameSamplePixelFormat pixelFormat,
        ArrayPool<byte> pool)
    {
        Buffer = buffer;
        ByteLength = byteLength;
        FrameIndex = frameIndex;
        Timestamp = timestamp;
        Width = width;
        Height = height;
        PixelFormat = pixelFormat;
        _pool = pool;
    }

    // Zero-based index among the emitted samples (not the source frame
    // number - many source frames are skipped between samples).
    public int FrameIndex { get; }

    // Exact source presentation timestamp of the sampled frame, as reported
    // by ffmpeg's showinfo filter - not a synthetic index/rate estimate.
    public TimeSpan Timestamp { get; }

    public int Width { get; }

    public int Height { get; }

    public FrameSamplePixelFormat PixelFormat { get; }

    // Rented buffer; only ReadOnlySpan is safe for callers to use directly.
    public byte[] Buffer { get; }

    public int ByteLength { get; }

    public ReadOnlySpan<byte> Span => Buffer.AsSpan(0, ByteLength);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _pool.Return(Buffer);
        }
    }
}
