using ExamForge.Domain.Generation;

namespace ExamForge.Tests.Generation;

public class SplitMix64RandomTests
{
    // Golden values come from an independent Python implementation of the
    // algorithm in docs/SPEC.md, not from this codebase. If these fail, the
    // generator changed and every stored seed stops reproducing its paper.
    [Fact]
    public void Seed42_ProducesTheGoldenSequence()
    {
        ulong[] expected =
        [
            0xBDD732262FEB6E95UL,
            0x28EFE333B266F103UL,
            0x47526757130F9F52UL,
            0x581CE1FF0E4AE394UL,
            0x09BC585A244823F2UL,
        ];

        Assert.Equal(expected, Take(new SplitMix64Random(42), 5));
    }

    // 0xE220A8397B1DCDAF is the widely cited first output of SplitMix64 seed 0.
    [Fact]
    public void Seed0_ProducesTheReferenceSequence()
    {
        ulong[] expected =
        [
            0xE220A8397B1DCDAFUL,
            0x6E789E6AA1B965F4UL,
            0x06C45D188009454FUL,
            0xF88BB8A8724C81ECUL,
            0x1B39896A51A8749BUL,
        ];

        Assert.Equal(expected, Take(new SplitMix64Random(0), 5));
    }

    [Fact]
    public void SameSeed_GivesSameSequence()
    {
        Assert.Equal(Take(new SplitMix64Random(123456789), 100), Take(new SplitMix64Random(123456789), 100));
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentSequences()
    {
        Assert.NotEqual(Take(new SplitMix64Random(1), 10), Take(new SplitMix64Random(2), 10));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(1000)]
    public void NextInt_StaysWithinZeroToN(int n)
    {
        var random = new SplitMix64Random(99);

        for (var i = 0; i < 10_000; i++)
        {
            var value = random.NextInt(n);
            Assert.InRange(value, 0, n - 1);
        }
    }

    [Fact]
    public void NextInt_ReachesEveryValueInASmallRange()
    {
        var random = new SplitMix64Random(7);
        var seen = new HashSet<int>();

        for (var i = 0; i < 1_000; i++)
        {
            seen.Add(random.NextInt(5));
        }

        Assert.Equal(new HashSet<int> { 0, 1, 2, 3, 4 }, seen);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NextInt_ThrowsWhenNIsNotPositive(int n)
    {
        var random = new SplitMix64Random(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(n));
    }

    [Fact]
    public void Shuffle_SameSeed_GivesSameOrder()
    {
        var first = Enumerable.Range(1, 50).ToList();
        var second = Enumerable.Range(1, 50).ToList();

        first.Shuffle(new SplitMix64Random(2026));
        second.Shuffle(new SplitMix64Random(2026));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Shuffle_ResultIsAPermutationOfTheInput()
    {
        var original = Enumerable.Range(1, 50).ToList();
        var shuffled = original.ToList();

        shuffled.Shuffle(new SplitMix64Random(5));

        Assert.NotEqual(original, shuffled);
        Assert.Equal(original, shuffled.Order().ToList());
    }

    [Fact]
    public void Shuffle_HandlesEmptyAndSingleItemLists()
    {
        var empty = new List<int>();
        var single = new List<int> { 9 };

        empty.Shuffle(new SplitMix64Random(1));
        single.Shuffle(new SplitMix64Random(1));

        Assert.Empty(empty);
        Assert.Equal([9], single);
    }

    private static ulong[] Take(IRandomSource random, int count)
    {
        var values = new ulong[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = random.NextUInt64();
        }

        return values;
    }
}
