using ExamForge.Domain.Generation;

namespace ExamForge.Infrastructure.Generation;

public sealed class SystemSeedSource : ISeedSource
{
    public long NextSeed() => Random.Shared.NextInt64();
}
