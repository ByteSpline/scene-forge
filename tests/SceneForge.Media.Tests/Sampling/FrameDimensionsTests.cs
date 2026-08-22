using SceneForge.Media.Sampling;

namespace SceneForge.Media.Tests.Sampling;

public class FrameDimensionsTests
{
    [Fact]
    public void ForTargetWidth_PreservesAspectRatioAndForcesEvenHeight()
    {
        // 1920x1080 -> 16:9; scaled to width 384 gives height 216 (already even).
        var dimensions = FrameDimensions.ForTargetWidth(1920, 1080, 384, FrameSamplePixelFormat.Bgr24);

        Assert.Equal(384, dimensions.Width);
        Assert.Equal(216, dimensions.Height);
        Assert.Equal(0, dimensions.Height % 2);
    }

    [Fact]
    public void ForTargetWidth_OddComputedHeight_RoundsDownToEven()
    {
        // 853x480 -> scaled to width 320 gives a raw height of 180.07..., forced even.
        var dimensions = FrameDimensions.ForTargetWidth(853, 480, 320, FrameSamplePixelFormat.Bgr24);

        Assert.Equal(0, dimensions.Height % 2);
    }

    [Fact]
    public void ByteLength_Bgr24_IsWidthTimesHeightTimesThreeBytes()
    {
        var dimensions = FrameDimensions.ForTargetWidth(640, 360, 320, FrameSamplePixelFormat.Bgr24);

        Assert.Equal(dimensions.Width * dimensions.Height * 3, dimensions.ByteLength);
    }

    [Fact]
    public void ByteLength_Gray8_IsWidthTimesHeight()
    {
        var dimensions = FrameDimensions.ForTargetWidth(640, 360, 320, FrameSamplePixelFormat.Gray8);

        Assert.Equal(dimensions.Width * dimensions.Height, dimensions.ByteLength);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, 1080)]
    public void ForTargetWidth_NonPositiveSourceDimensions_Throws(int sourceWidth, int sourceHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameDimensions.ForTargetWidth(sourceWidth, sourceHeight, 320, FrameSamplePixelFormat.Bgr24));
    }
}
