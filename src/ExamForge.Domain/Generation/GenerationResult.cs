using ExamForge.Domain.Enums;

namespace ExamForge.Domain.Generation;

/// <summary>
/// The outcome of one generation request: exactly one of the nested cases. The private
/// constructor means no other subclass can exist, so a switch over these four is complete.
/// </summary>
public abstract record GenerationResult
{
    private GenerationResult()
    {
    }

    /// <summary>The paper was saved. Its marks equal the requested total exactly.</summary>
    public sealed record Success(GeneratedPaper Paper) : GenerationResult;

    /// <summary>The bank cannot meet the specification. Nothing was saved.</summary>
    public sealed record Infeasible(IReadOnlyList<InfeasibleChapter> Failures) : GenerationResult;

    public sealed record SubjectNotFound(int SubjectId) : GenerationResult;

    /// <summary>
    /// Requested chapters that do not exist or belong to another subject, ascending. Checked
    /// after the subject (so an unknown subject is a 404 first) and before any solving.
    /// </summary>
    public sealed record ChaptersNotInSubject(IReadOnlyList<int> ChapterIds) : GenerationResult;

    /// <summary>Other generations for the subject kept committing first. Nothing was saved.</summary>
    public sealed record Conflict(int Attempts) : GenerationResult;
}

/// <summary>
/// A failed constraint plus how many marks of that chapter's active questions the history
/// window removed, so the API can say "8 marks short after excluding the last 3 papers".
/// </summary>
public sealed record InfeasibleChapter(ConstraintFailure Failure, int ExcludedMarks);

public sealed record GeneratedPaper(
    int Id,
    int SubjectId,
    string Title,
    int TotalMarks,
    long Seed,
    DifficultyMix AchievedMix,
    DateTimeOffset CreatedAt,
    IReadOnlyList<GeneratedQuestion> Questions);

/// <summary>One line of a saved paper. OrderIndex is 1-based and gap-free.</summary>
public sealed record GeneratedQuestion(
    int OrderIndex,
    int QuestionId,
    int ChapterId,
    string ChapterName,
    string Text,
    int Marks,
    Difficulty Difficulty);
