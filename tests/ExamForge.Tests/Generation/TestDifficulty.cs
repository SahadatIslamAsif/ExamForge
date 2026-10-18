using ExamForge.Domain.Enums;

namespace ExamForge.Tests.Generation;

internal static class TestDifficulty
{
    /// <summary>Easy, Medium, Hard, Easy, ... for index 0, 1, 2, 3, ... Never casts an int to the enum.</summary>
    public static Difficulty Cycle(int index) => (index % 3) switch
    {
        0 => Difficulty.Easy,
        1 => Difficulty.Medium,
        _ => Difficulty.Hard,
    };
}
