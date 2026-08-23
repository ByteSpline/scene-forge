using SceneForge.Media.Planning.Internal;

namespace SceneForge.Media.Tests.Planning;

public class ClipShuffleOrderTests
{
    [Fact]
    public void ComputeRanks_ZeroClips_ReturnsEmptyArray()
    {
        var ranks = ClipShuffleOrder.ComputeRanks(0, seed: 1);

        Assert.Empty(ranks);
    }

    [Fact]
    public void ComputeRanks_NegativeClipCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipShuffleOrder.ComputeRanks(-1, seed: 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(17)]
    [InlineData(200)]
    public void ComputeRanks_IsAPermutationOfZeroToCountMinusOne(int clipCount)
    {
        var ranks = ClipShuffleOrder.ComputeRanks(clipCount, seed: 42);

        Assert.Equal(clipCount, ranks.Length);
        Assert.Equal(Enumerable.Range(0, clipCount).OrderBy(x => x), ranks.OrderBy(x => x));
    }

    [Fact]
    public void ComputeRanks_SameSeedAndCount_IsDeterministic()
    {
        var first = ClipShuffleOrder.ComputeRanks(50, seed: 123);
        var second = ClipShuffleOrder.ComputeRanks(50, seed: 123);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeRanks_DifferentSeeds_TypicallyProduceDifferentOrder()
    {
        var first = ClipShuffleOrder.ComputeRanks(50, seed: 1);
        var second = ClipShuffleOrder.ComputeRanks(50, seed: 2);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ComputeRanks_ManySeeds_AlwaysProduceAValidPermutation()
    {
        for (var seed = 0; seed < 5000; seed++)
        {
            var ranks = ClipShuffleOrder.ComputeRanks(30, seed);

            Assert.Equal(Enumerable.Range(0, 30).OrderBy(x => x), ranks.OrderBy(x => x));
        }
    }
}
