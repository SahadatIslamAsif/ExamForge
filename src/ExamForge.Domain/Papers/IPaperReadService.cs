using ExamForge.Domain.Generation;

namespace ExamForge.Domain.Papers;

/// <summary>Reads saved papers. Separate from generation, which is the only writer.</summary>
public interface IPaperReadService
{
    /// <summary>The paper with its questions in OrderIndex order, or null if there is none.</summary>
    Task<GeneratedPaper?> GetAsync(int paperId, CancellationToken cancellationToken = default);

    /// <summary>The subject's papers, newest first; null if the subject does not exist.</summary>
    Task<IReadOnlyList<PaperSummary>?> ListForSubjectAsync(int subjectId, CancellationToken cancellationToken = default);
}

public sealed record PaperSummary(int Id, string Title, int TotalMarks, long Seed, DateTimeOffset CreatedAt);
