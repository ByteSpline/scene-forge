namespace SceneForge.Media.Sampling;

// Resolved, concrete output geometry for a sampling run - computed once
// from FrameSamplingOptions.AnalysisWidthPixels plus the source's aspect
// ratio, then held fixed for the run so every FrameSample's buffer is a
// known, identical size (required for ArrayPool reuse across frames).
internal readonly record struct FrameDimensions
{
    public FrameDimensions(int width, int height, FrameSamplePixelFormat pixelFormat)
    {
        Width = width;
        Height = height;
        PixelFormat = pixelFormat;
    }

    public int Width { get; }

    public int Height { get; }

    public FrameSamplePixelFormat PixelFormat { get; }

    public int ByteLength => Width * Height * PixelFormat.BytesPerPixel();

    // Scales sourceWidth x sourceHeight down to targetWidth, preserving
    // aspect ratio, with height forced to an even number of pixels (the
    // conventional constraint for chroma-subsampled decode paths upstream
    // of the raw output; harmless for the already-planar bgr24/gray output
    // here, but keeps the ffmpeg scale filter argument unambiguous).
    public static FrameDimensions ForTargetWidth(int sourceWidth, int sourceHeight, int targetWidth, FrameSamplePixelFormat pixelFormat)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), sourceWidth, "Source dimensions must be positive.");
        }

        var rawHeight = (int)Math.Round(sourceHeight * (targetWidth / (double)sourceWidth), MidpointRounding.AwayFromZero);
        var evenHeight = Math.Max(2, rawHeight - (rawHeight % 2));

        return new FrameDimensions(targetWidth, evenHeight, pixelFormat);
    }
}
