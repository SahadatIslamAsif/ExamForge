using ExamForge.Domain.Generation;
using ExamForge.Infrastructure.Generation;

namespace ExamForge.Tests.Generation;

public class SystemSeedSourceTests
{
    [Fact]
    public void Seeds_stay_within_javascripts_safe_integer_range()
    {
        var source = new SystemSeedSource();

        var seeds = Enumerable.Range(0, 10_000).Select(_ => source.NextSeed()).ToList();

        Assert.All(seeds, seed => Assert.InRange(seed, 0, SeedRange.MaxSafeInteger));
    }

    [Fact]
    public void Seeds_still_use_far_more_than_32_bits()
    {
        var source = new SystemSeedSource();

        // With 2^53 possible values, a seed above int.MaxValue is all but certain in 100 draws.
        Assert.Contains(Enumerable.Range(0, 100).Select(_ => source.NextSeed()), seed => seed > int.MaxValue);
    }
}
