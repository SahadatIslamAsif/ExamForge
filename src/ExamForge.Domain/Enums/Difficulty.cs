namespace ExamForge.Domain.Enums;

// Explicit values: the paper is ordered easy-to-hard by sorting on these numbers, so they must
// not depend on declaration order. Start at 1 so the default value 0 is not a valid difficulty.
public enum Difficulty
{
    Easy = 1,
    Medium = 2,
    Hard = 3,
}
