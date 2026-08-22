using System.Buffers;

namespace SceneForge.Media.Sampling;

// Reads fixed-size raw video frames one at a time from ffmpeg's stdout into
// pooled buffers. Never buffers more than one frame at a time itself - the
// caller (FrameSampler) owns bounding how many in-flight frames exist via
// the producer/consumer channel.
internal static class RawFrameStreamReader
{
    // Returns a rented buffer holding exactly frameByteLength valid bytes
    // (Buffer.Length may be larger; the pool does not guarantee exact
    // sizes), or null at a clean end-of-stream (zero bytes available before
    // the next frame starts). Throws FrameSamplingException if the stream
    // ends partway through a frame, since that indicates ffmpeg exited or
    // the pipe closed unexpectedly rather than a normal end of input.
    public static async Task<byte[]?> TryReadFrameAsync(Stream stream, int frameByteLength, ArrayPool<byte> pool, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(pool);

        if (frameByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameByteLength), frameByteLength, "Frame byte length must be positive.");
        }

        var buffer = pool.Rent(frameByteLength);
        var totalRead = 0;

        try
        {
            while (totalRead < frameByteLength)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, frameByteLength - totalRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }
        }
        catch
        {
            pool.Return(buffer);
            throw;
        }

        if (totalRead == 0)
        {
            pool.Return(buffer);
            return null;
        }

        if (totalRead < frameByteLength)
        {
            pool.Return(buffer);
            throw new FrameSamplingException(
                $"ffmpeg's raw video stream ended mid-frame ({totalRead} of {frameByteLength} bytes read); the decode process likely exited early or was killed.");
        }

        return buffer;
    }
}
