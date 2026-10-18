namespace ExamForge.Domain.Generation;

public static class SeedRange
{
    /// <summary>
    /// 2^53 - 1, JavaScript's Number.MAX_SAFE_INTEGER. JSON numbers are read as doubles by
    /// JavaScript clients, which cannot hold every integer above this exactly, so a larger seed
    /// could come back changed and no longer reproduce its paper.
    /// </summary>
    public const long MaxSafeInteger = 9_007_199_254_740_991;
}
