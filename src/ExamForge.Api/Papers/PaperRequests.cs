namespace ExamForge.Api.Papers;

// Every property is nullable on purpose: a missing field is reported by our validator, with
// our wording and field name, instead of being rejected earlier by the framework.
public sealed record GeneratePaperRequest(
    int? SubjectId,
    string? Title,
    int? TotalMarks,
    IReadOnlyList<ChapterMarksRequest?>? ChapterDistribution,
    DifficultyMixRequest? DifficultyMix,
    int? ExcludeLastNPapers,
    long? Seed);

public sealed record ChapterMarksRequest(int? ChapterId, int? Marks);

public sealed record DifficultyMixRequest(double? Easy, double? Medium, double? Hard);
