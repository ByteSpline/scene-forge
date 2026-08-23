using SceneForge.Media.Rendering.Internal;

namespace SceneForge.Media.Tests.Rendering;

public class RenderProgressParserTests
{
    [Fact]
    public void Accept_IntermediateKeyLines_ReturnsNull()
    {
        var parser = new RenderProgressParser();

        Assert.Null(parser.Accept("frame=10", TimeSpan.Zero));
        Assert.Null(parser.Accept("fps=25.0", TimeSpan.Zero));
        Assert.Null(parser.Accept("out_time_us=400000", TimeSpan.Zero));
        Assert.Null(parser.Accept("speed=1.5x", TimeSpan.Zero));
    }

    [Fact]
    public void Accept_ProgressContinueLine_CompletesOneUpdate()
    {
        var parser = new RenderProgressParser();
        parser.Accept("frame=10", TimeSpan.Zero);
        parser.Accept("fps=25.0", TimeSpan.Zero);
        parser.Accept("out_time_us=400000", TimeSpan.Zero);
        parser.Accept("speed=2x", TimeSpan.Zero);

        var progress = parser.Accept("progress=continue", TimeSpan.FromSeconds(1));

        Assert.NotNull(progress);
        Assert.Equal(10, progress!.FrameNumber);
        Assert.Equal(25.0, progress.FramesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(0.4), progress.OutTime);
        Assert.Equal(2.0, progress.Speed);
        Assert.Equal(TimeSpan.FromSeconds(1), progress.Elapsed);
        Assert.False(progress.IsFinished);
        Assert.Null(progress.EstimatedTimeRemaining);
    }

    [Fact]
    public void Accept_ProgressEndLine_MarksFinished()
    {
        var parser = new RenderProgressParser();
        parser.Accept("frame=100", TimeSpan.Zero);
        parser.Accept("out_time_us=4000000", TimeSpan.Zero);

        var progress = parser.Accept("progress=end", TimeSpan.FromSeconds(4));

        Assert.NotNull(progress);
        Assert.True(progress!.IsFinished);
    }

    [Fact]
    public void Accept_SecondBlock_DoesNotCarryStateFromFirstBlock()
    {
        var parser = new RenderProgressParser();
        parser.Accept("frame=10", TimeSpan.Zero);
        parser.Accept("speed=2x", TimeSpan.Zero);
        parser.Accept("progress=continue", TimeSpan.Zero);

        parser.Accept("frame=20", TimeSpan.Zero);
        var second = parser.Accept("progress=continue", TimeSpan.FromSeconds(2));

        Assert.NotNull(second);
        Assert.Equal(20, second!.FrameNumber);
        Assert.Null(second.Speed);
    }

    [Fact]
    public void Accept_MalformedLineWithoutEquals_ReturnsNullAndIsIgnored()
    {
        var parser = new RenderProgressParser();

        Assert.Null(parser.Accept("not a key value line", TimeSpan.Zero));

        var progress = parser.Accept("progress=continue", TimeSpan.Zero);
        Assert.NotNull(progress);
        Assert.Equal(0, progress!.FrameNumber);
    }

    [Fact]
    public void Accept_MissingOutTime_DefaultsToZero()
    {
        var parser = new RenderProgressParser();
        parser.Accept("frame=1", TimeSpan.Zero);

        var progress = parser.Accept("progress=continue", TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, progress!.OutTime);
    }
}
