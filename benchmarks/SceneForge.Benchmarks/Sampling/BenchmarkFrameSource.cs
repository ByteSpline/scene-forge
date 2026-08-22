namespace SceneForge.Benchmarks.Sampling;

// Generates synthetic raw-frame bytes on the fly - no real ffmpeg process,
// no real video file - so the benchmark measures FrameSampler's own
// pooling/channel/timestamp-correlation pipeline in isolation, not process
// I/O or decode cost.
internal sealed class BenchmarkFrameSource : Stream
{
    private readonly int _frameByteLength;
    private readonly int _totalFrames;
    private int _frameIndex;
    private int _positionWithinFrame;

    public BenchmarkFrameSource(int frameByteLength, int totalFrames)
    {
        _frameByteLength = frameByteLength;
        _totalFrames = totalFrames;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_frameIndex >= _totalFrames)
        {
            return ValueTask.FromResult(0);
        }

        var remainingInFrame = _frameByteLength - _positionWithinFrame;
        var toWrite = Math.Min(remainingInFrame, buffer.Length);
        buffer.Span[..toWrite].Fill(unchecked((byte)_frameIndex));

        _positionWithinFrame += toWrite;
        if (_positionWithinFrame == _frameByteLength)
        {
            _positionWithinFrame = 0;
            _frameIndex++;
        }

        return ValueTask.FromResult(toWrite);
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
