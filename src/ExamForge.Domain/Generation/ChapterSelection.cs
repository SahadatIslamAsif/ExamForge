namespace ExamForge.Domain.Generation;

/// <summary>The questions chosen for one chapter. Final paper ordering is a later step.</summary>
public sealed record ChapterSelection(int ChapterId, IReadOnlyList<SolverQuestion> Questions);
