using System.Text.Json;
using ExamForge.Domain.Generation;
using ExamForge.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Tests.GenerationService;

[Collection(PostgresCollection.Name)]
public class GenerationSuccessTests(PostgresFixture postgres) : GenerationTestBase(postgres)
{
    private static readonly int[] ChapterBank = [1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 1, 2, 3];

    [Fact]
    public async Task A_fifty_mark_paper_over_three_chapters_is_exact_and_saved_in_order()
    {
        var bank = await SeedBankAsync(ChapterBank, ChapterBank, ChapterBank);
        int[] targets = [20, 20, 10];
        var spec = Spec(bank, targets, seed: 42);

        var paper = AssertSuccess(await NewService().GenerateAsync(spec));

        // Exact total and exact per-chapter marks.
        Assert.Equal(50, paper.TotalMarks);
        Assert.Equal(50, paper.Questions.Sum(q => q.Marks));
        for (var i = 0; i < targets.Length; i++)
        {
            Assert.Equal(targets[i], paper.Questions.Where(q => q.ChapterId == bank.ChapterIds[i]).Sum(q => q.Marks));
        }

        Assert.Equal(Enumerable.Range(1, paper.Questions.Count), paper.Questions.Select(q => q.OrderIndex));
        Assert.All(paper.Questions, q => Assert.False(string.IsNullOrWhiteSpace(q.Text)));
        Assert.Equal(42, paper.Seed);

        await using var context = NewContext();
        var stored = await context.Papers.AsNoTracking().SingleAsync(p => p.Id == paper.Id);
        Assert.Equal(42, stored.Seed);
        Assert.Equal(50, stored.TotalMarks);
        Assert.Equal(paper.CreatedAt, stored.CreatedAt);

        // SpecJson is the specification with the seed it was generated with.
        var storedSpec = JsonSerializer.Deserialize<PaperSpecification>(stored.SpecJson, JsonSerializerOptions.Web)!;
        Assert.Equal(spec.SubjectId, storedSpec.SubjectId);
        Assert.Equal(spec.Title, storedSpec.Title);
        Assert.Equal(spec.ChapterDistribution, storedSpec.ChapterDistribution);
        Assert.Equal(spec.DifficultyMix, storedSpec.DifficultyMix);
        Assert.Equal(42, storedSpec.Seed);

        var storedLines = await context.PaperQuestions.AsNoTracking()
            .Where(pq => pq.PaperId == paper.Id)
            .OrderBy(pq => pq.OrderIndex)
            .Select(pq => new { pq.OrderIndex, pq.QuestionId })
            .ToListAsync();
        Assert.Equal(paper.Questions.Select(q => new { q.OrderIndex, q.QuestionId }), storedLines);

        var subject = await context.Subjects.AsNoTracking().SingleAsync(s => s.Id == bank.SubjectId);
        Assert.Equal(paper.CreatedAt, subject.LastGeneratedAt);
    }

    [Fact]
    public async Task An_unknown_subject_is_reported_and_nothing_is_saved()
    {
        var spec = new PaperSpecification(999_999, "Nope", 5, [new ChapterMarks(1, 5)], DefaultMix, 0, 42);

        var result = await NewService().GenerateAsync(spec);

        Assert.Equal(999_999, Assert.IsType<GenerationResult.SubjectNotFound>(result).SubjectId);
    }
}
