using ExamForge.Domain.Generation;

namespace ExamForge.Infrastructure.Generation;

public sealed class SystemSeedSource : ISeedSource
{
    // 0..MaxSafeInteger inclusive (the upper bound is exclusive), so every generated seed
    // survives a round trip through a JavaScript client.
    public long NextSeed() => Random.Shared.NextInt64(0, SeedRange.MaxSafeInteger + 1);
}
