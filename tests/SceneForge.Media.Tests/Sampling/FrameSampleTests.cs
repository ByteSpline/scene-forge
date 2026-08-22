using System.Buffers;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Tests.Sampling;

public class FrameSampleTests
{
    [Fact]
    public void Dispose_ReturnsBufferToPool()
    {
        var pool = ArrayPool<byte>.Create(maxArrayLength: 64, maxArraysPerBucket: 1);
        var rented = pool.Rent(16);
        var sample = CreateSample(rented, pool);

        sample.Dispose();

        // The pool now has exactly the one bucket slot free again; renting
        // the same size should hand back the same array instance.
        var reRented = pool.Rent(16);
        Assert.Same(rented, reRented);
    }

    [Fact]
    public void Dispose_CalledTwice_ReturnsBufferToPoolOnlyOnce()
    {
        var returnCount = 0;
        var pool = new CountingArrayPool(() => returnCount++);
        var buffer = new byte[16];
        var sample = CreateSample(buffer, pool);

        sample.Dispose();
        sample.Dispose();

        Assert.Equal(1, returnCount);
    }

    [Fact]
    public void Span_ExposesOnlyByteLengthNotFullBufferCapacity()
    {
        var buffer = new byte[32];
        var sample = CreateSample(buffer, ArrayPool<byte>.Shared, byteLength: 10);

        Assert.Equal(10, sample.Span.Length);
    }

    private static FrameSample CreateSample(byte[] buffer, ArrayPool<byte> pool, int byteLength = 16) =>
        new(buffer, byteLength, frameIndex: 0, timestamp: TimeSpan.Zero, width: 4, height: 4, pixelFormat: FrameSamplePixelFormat.Gray8, pool);

    private sealed class CountingArrayPool : ArrayPool<byte>
    {
        private readonly Action _onReturn;

        public CountingArrayPool(Action onReturn)
        {
            _onReturn = onReturn;
        }

        public override byte[] Rent(int minimumLength) => new byte[minimumLength];

        public override void Return(byte[] array, bool clearArray = false) => _onReturn();
    }
}
