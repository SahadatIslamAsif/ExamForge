using ExamForge.Domain.Enums;

namespace ExamForge.Api.Catalog;

// Requests: every property nullable so our validator reports missing fields. Difficulty and
// Type are strings here, parsed by the validator, so "Hardd" is a 400 on "difficulty" rather
// than a JSON deserialisation error.
public sealed record CreateSubjectRequest(string? Name, string? Level);

public sealed record CreateChapterRequest(int? SubjectId, string? Name, int? OrderIndex);

public sealed record CreateQuestionRequest(
    int? ChapterId,
    string? Text,
    int? Marks,
    string? Difficulty,
    string? Type,
    bool? IsActive);

/// <summary>PATCH body: a field that is absent (or null) means "leave unchanged".</summary>
public sealed record UpdateQuestionRequest(
    int? ChapterId,
    string? Text,
    int? Marks,
    string? Difficulty,
    string? Type,
    bool? IsActive);

public sealed record SubjectResponse(int Id, string Name, string Level);

public sealed record ChapterResponse(int Id, int SubjectId, string Name, int OrderIndex);

public sealed record QuestionResponse(
    int Id,
    int ChapterId,
    string Text,
    int Marks,
    Difficulty Difficulty,
    QuestionType Type,
    bool IsActive,
    DateTime CreatedAt,
    int UsedInPapers);
