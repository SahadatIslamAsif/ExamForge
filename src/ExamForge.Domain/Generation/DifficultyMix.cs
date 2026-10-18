namespace ExamForge.Domain.Generation;

/// <summary>Shares of a paper's marks by difficulty. The requested mix and the achieved mix are both this type.</summary>
public sealed record DifficultyMix(double Easy, double Medium, double Hard)
{
    /// <summary>The mix of a set of marks. All zeros when there are no marks at all.</summary>
    public static DifficultyMix FromMarks(int easy, int medium, int hard)
    {
        var total = easy + medium + hard;
        return total == 0
            ? new DifficultyMix(0, 0, 0)
            : new DifficultyMix((double)easy / total, (double)medium / total, (double)hard / total);
    }

    /// <summary>L1 distance: the sum of the absolute differences per level.</summary>
    public double L1Distance(DifficultyMix other) =>
        Math.Abs(Easy - other.Easy) + Math.Abs(Medium - other.Medium) + Math.Abs(Hard - other.Hard);
}
