namespace ExamForge.Domain.Generation;

/// <summary>
/// Either a selection or the list of constraints the bank cannot meet, never both. On failure
/// Chapters and OrderedQuestions are empty and AchievedMix is null, so a caller cannot mistake it for a paper.
/// </summary>
public sealed class SolverResult
{
    private SolverResult(
        IReadOnlyList<ConstraintFailure> failures,
        IReadOnlyList<ChapterSelection> chapters,
        IReadOnlyList<OrderedQuestion> orderedQuestions,
        DifficultyMix? achievedMix)
    {
        Failures = failures;
        Chapters = chapters;
        OrderedQuestions = orderedQuestions;
        AchievedMix = achievedMix;
    }

    public bool IsSuccess => Failures.Count == 0;

    public IReadOnlyList<ConstraintFailure> Failures { get; }

    /// <summary>Selected questions per chapter, in chapter OrderIndex order.</summary>
    public IReadOnlyList<ChapterSelection> Chapters { get; }

    /// <summary>The paper as one ordered list: chapter OrderIndex, difficulty ascending, question Id.</summary>
    public IReadOnlyList<OrderedQuestion> OrderedQuestions { get; }

    /// <summary>The difficulty mix of the ordered list, measured in marks. Null on failure.</summary>
    public DifficultyMix? AchievedMix { get; }

    public static SolverResult Succeeded(
        IReadOnlyList<ChapterSelection> chapters,
        IReadOnlyList<OrderedQuestion> orderedQuestions,
        DifficultyMix achievedMix) =>
        new([], chapters, orderedQuestions, achievedMix);

    public static SolverResult Failed(IReadOnlyList<ConstraintFailure> failures) =>
        new(failures, [], [], null);
}
