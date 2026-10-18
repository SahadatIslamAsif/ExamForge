namespace ExamForge.Domain.Generation;

/// <summary>Marks one chapter must contribute to the paper.</summary>
public sealed record ChapterMarks(int ChapterId, int Marks);

/// <summary>
/// What a caller asks the generator for. It is validated at the API boundary before it gets
/// here. Seed is null when the caller wants a random paper; the generator then picks one,
/// stores it on the paper and returns it, so the paper can be reproduced later.
/// </summary>
public sealed record PaperSpecification(
    int SubjectId,
    string Title,
    int TotalMarks,
    IReadOnlyList<ChapterMarks> ChapterDistribution,
    DifficultyMix DifficultyMix,
    int ExcludeLastNPapers,
    long? Seed);
