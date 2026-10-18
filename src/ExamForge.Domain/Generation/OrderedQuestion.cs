using ExamForge.Domain.Enums;

namespace ExamForge.Domain.Generation;

/// <summary>One line of the finished paper. OrderIndex is 1-based and gap-free.</summary>
public sealed record OrderedQuestion(
    int OrderIndex,
    int QuestionId,
    int ChapterId,
    string ChapterName,
    int Marks,
    Difficulty Difficulty);
