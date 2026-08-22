using SceneForge.Media.Processes;

namespace SceneForge.Media.Tests.Processes;

public class BoundedTextCollectorTests
{
    [Fact]
    public void AppendLine_UnderBudget_JoinsLinesWithNewline()
    {
        var collector = new BoundedTextCollector(1024);

        collector.AppendLine("first");
        collector.AppendLine("second");

        Assert.Equal("first\nsecond\n", collector.ToString());
    }

    [Fact]
    public void AppendLine_ExceedsBudget_TruncatesAndStopsGrowing()
    {
        var collector = new BoundedTextCollector(20);

        for (var i = 0; i < 100; i++)
        {
            collector.AppendLine($"line-{i}");
        }

        var result = collector.ToString();

        Assert.EndsWith("...[truncated]", result);
        Assert.True(result.Length <= 20 + "...[truncated]".Length);
    }

    [Fact]
    public void AppendLine_AfterTruncation_DoesNotAppendFurther()
    {
        var collector = new BoundedTextCollector(10);

        collector.AppendLine("0123456789and-more");
        var lengthAfterFirstOverflow = collector.ToString().Length;
        collector.AppendLine("another line that should be dropped entirely");

        Assert.Equal(lengthAfterFirstOverflow, collector.ToString().Length);
    }
}
