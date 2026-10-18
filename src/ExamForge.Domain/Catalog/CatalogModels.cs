using ExamForge.Domain.Enums;

namespace ExamForge.Domain.Catalog;

public sealed record SubjectInfo(int Id, string Name, string Level);

public sealed record NewSubject(string Name, string Level);

public sealed record ChapterInfo(int Id, int SubjectId, string Name, int OrderIndex);

public sealed record NewChapter(int SubjectId, string Name, int OrderIndex);

/// <summary>UsedInPapers counts the papers the question appears in; above 0 it is locked except IsActive.</summary>
public sealed record QuestionInfo(
    int Id,
    int ChapterId,
    string Text,
    int Marks,
    Difficulty Difficulty,
    QuestionType Type,
    bool IsActive,
    DateTimeOffset CreatedAt,
    int UsedInPapers);

public sealed record NewQuestion(int ChapterId, string Text, int Marks, Difficulty Difficulty, QuestionType Type, bool IsActive);

/// <summary>A partial update: null means "leave unchanged".</summary>
public sealed record QuestionChanges(
    int? ChapterId,
    string? Text,
    int? Marks,
    Difficulty? Difficulty,
    QuestionType? Type,
    bool? IsActive);
