using System.Buffers;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Sampling;

public class RawFrameStreamReaderTests
{
    [Fact]
    public async Task TryReadFrameAsync_FullFrameAvailable_ReturnsBufferWithExactBytes()
    {
        await using var stream = new SyntheticFrameSourceStream(frameByteLength: 12, totalFrames: 1);

        var buffer = await RawFrameStreamReader.TryReadFrameAsync(stream, 12, ArrayPool<byte>.Shared, CancellationToken.None);

        Assert.NotNull(buffer);
        Assert.True(buffer!.Length >= 12);
        Assert.All(buffer.AsSpan(0, 12).ToArray(), b => Assert.Equal(0, b));
        ArrayPool<byte>.Shared.Return(buffer);
    }

    [Fact]
    public async Task TryReadFrameAsync_CleanEndOfStream_ReturnsNull()
    {
        await using var stream = new SyntheticFrameSourceStream(frameByteLength: 12, totalFrames: 0);

        var buffer = await RawFrameStreamReader.TryReadFrameAsync(stream, 12, ArrayPool<byte>.Shared, CancellationToken.None);

        Assert.Null(buffer);
    }

    [Fact]
    public async Task TryReadFrameAsync_StreamEndsMidFrame_ThrowsFrameSamplingException()
    {
        var partialFrame = new byte[] { 1, 2, 3 };
        await using var stream = new MemoryStream(partialFrame);

        var exception = await Assert.ThrowsAsync<FrameSamplingException>(
            () => RawFrameStreamReader.TryReadFrameAsync(stream, frameByteLength: 12, ArrayPool<byte>.Shared, CancellationToken.None));

        Assert.Contains("mid-frame", exception.Message);
    }

    [Fact]
    public async Task TryReadFrameAsync_MultipleFrames_ReadsEachFrameSequentially()
    {
        await using var stream = new SyntheticFrameSourceStream(frameByteLength: 4, totalFrames: 3);

        var first = await RawFrameStreamReader.TryReadFrameAsync(stream, 4, ArrayPool<byte>.Shared, CancellationToken.None);
        var second = await RawFrameStreamReader.TryReadFrameAsync(stream, 4, ArrayPool<byte>.Shared, CancellationToken.None);
        var third = await RawFrameStreamReader.TryReadFrameAsync(stream, 4, ArrayPool<byte>.Shared, CancellationToken.None);
        var fourth = await RawFrameStreamReader.TryReadFrameAsync(stream, 4, ArrayPool<byte>.Shared, CancellationToken.None);

        Assert.Equal(0, first![0]);
        Assert.Equal(1, second![0]);
        Assert.Equal(2, third![0]);
        Assert.Null(fourth);

        ArrayPool<byte>.Shared.Return(first);
        ArrayPool<byte>.Shared.Return(second);
        ArrayPool<byte>.Shared.Return(third);
    }

    [Fact]
    public async Task TryReadFrameAsync_ZeroOrNegativeFrameByteLength_Throws()
    {
        await using var stream = new SyntheticFrameSourceStream(frameByteLength: 4, totalFrames: 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => RawFrameStreamReader.TryReadFrameAsync(stream, 0, ArrayPool<byte>.Shared, CancellationToken.None));
    }
}
