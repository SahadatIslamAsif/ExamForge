using ExamForge.Domain.Entities;
using ExamForge.Domain.Enums;
using ExamForge.Domain.Generation;
using ExamForge.Infrastructure.Generation;
using ExamForge.Infrastructure.Persistence;
using ExamForge.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Tests.GenerationService;

// A subject with its chapters (in OrderIndex order) and each chapter's question Ids (in the
// order of the marks they were created from).
public sealed record Bank(int SubjectId, IReadOnlyList<int> ChapterIds, IReadOnlyList<IReadOnlyList<int>> QuestionIds);

public abstract class GenerationTestBase(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    protected static readonly DifficultyMix DefaultMix = new(0.3, 0.5, 0.2);

    protected FakeTimeProvider Clock { get; } = new();

    protected PaperGenerationService NewService(ISeedSource? seeds = null, Action<ExamForgeDbContext>? onSaving = null) =>
        new(new TestDbContextFactory(Database, onSaving), Clock, seeds ?? new SequenceSeedSource(1));

    // One chapter per array; one question per mark value. Difficulty cycles Easy, Medium, Hard.
    protected async Task<Bank> SeedBankAsync(params int[][] marksPerChapter)
    {
        await using var context = NewContext();
        var subject = new Subject { Name = $"subject-{Guid.NewGuid():N}", Level = "O Level" };
        var chapters = marksPerChapter.Select((marks, i) => new Chapter
        {
            Subject = subject,
            Name = $"Chapter {i + 1}",
            OrderIndex = i + 1,
            Questions = marks.Select((m, j) => new Question
            {
                Text = $"Chapter {i + 1} question {j + 1}",
                Marks = m,
                Difficulty = (Difficulty)(j % 3 + 1),
                Type = QuestionType.Short,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            }).ToList(),
        }).ToList();

        context.Chapters.AddRange(chapters);
        await context.SaveChangesAsync();

        return new Bank(
            subject.Id,
            chapters.Select(c => c.Id).ToList(),
            chapters.Select(c => (IReadOnlyList<int>)c.Questions.Select(q => q.Id).ToList()).ToList());
    }

    protected static PaperSpecification Spec(Bank bank, int[] marksPerChapter, long? seed = 42, int excludeLastNPapers = 0) =>
        new(
            bank.SubjectId,
            "Practice paper",
            marksPerChapter.Sum(),
            bank.ChapterIds.Zip(marksPerChapter, (id, marks) => new ChapterMarks(id, marks)).ToList(),
            DefaultMix,
            excludeLastNPapers,
            seed);

    // Writes a paper straight to the database, to set up history with exact timestamps.
    protected async Task SeedHistoryPaperAsync(int subjectId, DateTimeOffset createdAt, params int[] questionIds)
    {
        await using var context = NewContext();
        context.Papers.Add(new Paper
        {
            SubjectId = subjectId,
            Title = "History",
            TotalMarks = 1,
            Seed = 0,
            SpecJson = "{}",
            CreatedAt = createdAt,
            PaperQuestions = questionIds.Select((id, i) => new PaperQuestion { QuestionId = id, OrderIndex = i + 1 }).ToList(),
        });
        await context.SaveChangesAsync();
    }

    protected async Task<int> PaperCountAsync(int subjectId)
    {
        await using var context = NewContext();
        return await context.Papers.CountAsync(p => p.SubjectId == subjectId);
    }

    protected static GeneratedPaper AssertSuccess(GenerationResult result) =>
        Assert.IsType<GenerationResult.Success>(result).Paper;

    protected static IReadOnlyList<InfeasibleChapter> AssertInfeasible(GenerationResult result) =>
        Assert.IsType<GenerationResult.Infeasible>(result).Failures;

    protected static List<int> Ids(GeneratedPaper paper) => paper.Questions.Select(q => q.QuestionId).ToList();

    protected static DateTimeOffset Day(int day) => new(2025, 6, day, 12, 0, 0, TimeSpan.Zero);
}
