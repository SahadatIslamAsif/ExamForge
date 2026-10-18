namespace ExamForge.Domain.Generation;

/// <summary>
/// Either a selection or the list of constraints the bank cannot meet, never both. On failure
/// Chapters is empty and AchievedMix is null, so a caller cannot mistake it for a paper.
/// </summary>
public sealed class SolverResult
{
    private SolverResult(
        IReadOnlyList<ConstraintFailure> failures,
        IReadOnlyList<ChapterSelection> chapters,
        DifficultyMix? achievedMix)
    {
        Failures = failures;
        Chapters = chapters;
        AchievedMix = achievedMix;
    }

    public bool IsSuccess => Failures.Count == 0;

    public IReadOnlyList<ConstraintFailure> Failures { get; }

    /// <summary>Selected questions per chapter, in chapter OrderIndex order.</summary>
    public IReadOnlyList<ChapterSelection> Chapters { get; }

    /// <summary>The difficulty mix of the whole selection, measured in marks. Null on failure.</summary>
    public DifficultyMix? AchievedMix { get; }

    public static SolverResult Succeeded(IReadOnlyList<ChapterSelection> chapters, DifficultyMix achievedMix) =>
        new([], chapters, achievedMix);

    public static SolverResult Failed(IReadOnlyList<ConstraintFailure> failures) =>
        new(failures, [], null);
}
