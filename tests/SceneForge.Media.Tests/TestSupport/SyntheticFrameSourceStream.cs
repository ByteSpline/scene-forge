namespace SceneForge.Media.Tests.TestSupport;

// Generates raw "video" bytes for totalFrames frames of frameByteLength each
// on the fly - never allocates a backing buffer for more than the current
// read call - so tests can simulate an arbitrarily long "video" (thousands
// of frames) without the test itself using proportional memory.
internal sealed class SyntheticFrameSourceStream : Stream
{
    private readonly int _frameByteLength;
    private readonly int _totalFrames;
    private readonly Func<int, ValueTask>? _beforeFrame;
    private int _frameIndex;
    private int _positionWithinFrame;

    public SyntheticFrameSourceStream(int frameByteLength, int totalFrames, Func<int, ValueTask>? beforeFrame = null)
    {
        _frameByteLength = frameByteLength;
        _totalFrames = totalFrames;
        _beforeFrame = beforeFrame;
    }

    // Number of complete frames the reader has fully consumed so far.
    public int FramesProduced => _frameIndex;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();

        if (_frameIndex >= _totalFrames)
        {
            return 0;
        }

        if (_positionWithinFrame == 0 && _beforeFrame is not null)
        {
            await _beforeFrame(_frameIndex).ConfigureAwait(false);
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

        return toWrite;
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
