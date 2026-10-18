using ExamForge.Domain.Enums;
using ExamForge.Domain.Generation;
using ExamForge.Domain.Papers;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Infrastructure.Papers;

public sealed class PaperReadService(IDbContextFactory<ExamForgeDbContext> contextFactory) : IPaperReadService
{
    public async Task<GeneratedPaper?> GetAsync(int paperId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var paper = await context.Papers.AsNoTracking()
            .Where(p => p.Id == paperId)
            .Select(p => new { p.Id, p.SubjectId, p.Title, p.TotalMarks, p.Seed, p.CreatedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (paper is null)
        {
            return null;
        }

        var questions = await context.PaperQuestions.AsNoTracking()
            .Where(pq => pq.PaperId == paperId)
            .OrderBy(pq => pq.OrderIndex)
            .Select(pq => new GeneratedQuestion(
                pq.OrderIndex,
                pq.QuestionId,
                pq.Question.ChapterId,
                pq.Question.Chapter.Name,
                pq.Question.Text,
                pq.Question.Marks,
                pq.Question.Difficulty))
            .ToListAsync(cancellationToken);

        // Recomputed from the stored lines the same way the solver measures it, so a GET
        // reports exactly what the 201 reported.
        var achieved = DifficultyMix.FromMarks(
            MarksOf(questions, Difficulty.Easy), MarksOf(questions, Difficulty.Medium), MarksOf(questions, Difficulty.Hard));

        return new GeneratedPaper(
            paper.Id, paper.SubjectId, paper.Title, paper.TotalMarks, paper.Seed, achieved, paper.CreatedAt, questions);
    }

    public async Task<IReadOnlyList<PaperSummary>?> ListForSubjectAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (!await context.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken))
        {
            return null;
        }

        return await context.Papers.AsNoTracking()
            .Where(p => p.SubjectId == subjectId)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Select(p => new PaperSummary(p.Id, p.Title, p.TotalMarks, p.Seed, p.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private static int MarksOf(IEnumerable<GeneratedQuestion> questions, Difficulty difficulty) =>
        questions.Where(q => q.Difficulty == difficulty).Sum(q => q.Marks);
}
