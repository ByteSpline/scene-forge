namespace SceneForge.Media.Sampling;

public enum FrameSamplePixelFormat
{
    // 8-bit blue/green/red, 3 bytes per pixel - ffmpeg pix_fmt "bgr24".
    Bgr24,

    // 8-bit single-channel luma, 1 byte per pixel - ffmpeg pix_fmt "gray".
    Gray8,
}

internal static class FrameSamplePixelFormatExtensions
{
    public static int BytesPerPixel(this FrameSamplePixelFormat pixelFormat) => pixelFormat switch
    {
        FrameSamplePixelFormat.Bgr24 => 3,
        FrameSamplePixelFormat.Gray8 => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "Unknown pixel format."),
    };

    // ffmpeg's -pix_fmt argument value for this format.
    public static string ToFfmpegPixelFormatName(this FrameSamplePixelFormat pixelFormat) => pixelFormat switch
    {
        FrameSamplePixelFormat.Bgr24 => "bgr24",
        FrameSamplePixelFormat.Gray8 => "gray",
        _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "Unknown pixel format."),
    };
}
