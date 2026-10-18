using ExamForge.Domain.Generation;
using ExamForge.Domain.Papers;

namespace ExamForge.Api.Papers;

/// <summary>Domain results to response DTOs, including the human-readable 422 details.</summary>
public static class PaperMapping
{
    public const string InfeasibleError = "INFEASIBLE_SPECIFICATION";
    public const string InfeasibleMessage = "Cannot satisfy the requested mark distribution.";
    public const string ConflictError = "CONCURRENT_GENERATION";
    public const string ConflictMessage =
        "Other papers for this subject were being generated at the same time, and this request lost the race "
        + "on every attempt. The specification is fine; try again.";

    public static PaperResponse ToResponse(GeneratedPaper paper) =>
        new(
            paper.Id,
            paper.SubjectId,
            paper.Title,
            paper.TotalMarks,
            paper.Seed,
            new DifficultyMixResponse(paper.AchievedMix.Easy, paper.AchievedMix.Medium, paper.AchievedMix.Hard),
            paper.Questions
                .Select(q => new PaperQuestionResponse(q.OrderIndex, q.QuestionId, q.ChapterId, q.ChapterName, q.Text, q.Marks, q.Difficulty))
                .ToList(),
            paper.CreatedAt.UtcDateTime);

    public static PaperSummaryResponse ToResponse(PaperSummary summary) =>
        new(summary.Id, summary.Title, summary.TotalMarks, summary.Seed, summary.CreatedAt.UtcDateTime);

    public static InfeasibleResponse ToResponse(GenerationResult.Infeasible infeasible, int excludeLastNPapers) =>
        new(InfeasibleError, InfeasibleMessage, infeasible.Failures.Select(f => ToFailure(f, excludeLastNPapers)).ToList());

    private static FailureResponse ToFailure(InfeasibleChapter chapter, int excludeLastNPapers)
    {
        var failure = chapter.Failure;
        var (constraint, detail) = failure.Constraint switch
        {
            FailureConstraint.ChapterDistribution when chapter.ExcludedMarks > 0 =>
                ("chapterDistribution",
                    $"{MarksText(failure.ShortfallMarks)} short after excluding questions used in {LastPapersText(excludeLastNPapers)}."),
            FailureConstraint.ChapterDistribution =>
                ("chapterDistribution",
                    $"{MarksText(failure.ShortfallMarks)} short: the chapter's active questions total {MarksText(failure.AvailableMarks)}."),
            FailureConstraint.ExactMarks =>
                ("exactMarks",
                    $"Eligible questions total {MarksText(failure.AvailableMarks)}, but no combination sums to exactly {failure.RequestedMarks}."),
            _ => throw new ArgumentOutOfRangeException(nameof(chapter), failure.Constraint, "Unknown constraint."),
        };

        return new FailureResponse(
            constraint, failure.ChapterId, failure.ChapterName, failure.RequestedMarks, failure.AvailableMarks, detail);
    }

    private static string MarksText(int marks) => marks == 1 ? "1 mark" : $"{marks} marks";

    private static string LastPapersText(int n) => n == 1 ? "the last paper" : $"the last {n} papers";
}
