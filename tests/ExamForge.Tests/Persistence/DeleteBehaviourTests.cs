using Microsoft.EntityFrameworkCore;

namespace ExamForge.Tests.Persistence;

// ON DELETE RESTRICT raises SqlState 23001 (restrict_violation). 23503 (foreign_key_violation)
// is what the default NO ACTION raises, so expecting 23001 pins the test to Restrict itself.
// ExecuteDeleteAsync sends a plain DELETE without loading or tracking entities, so what
// is tested here is the database's foreign keys, not EF's own cascade logic.
[Collection(PostgresCollection.Name)]
public class DeleteBehaviourTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    [Fact]
    public async Task A_question_used_in_a_paper_cannot_be_deleted()
    {
        var subjectId = await SeedSubjectAsync();
        var questionId = await SeedQuestionAsync(await SeedChapterAsync(subjectId));
        await AddToPaperAsync(await SeedPaperAsync(subjectId), questionId, orderIndex: 1);

        await AssertPostgresErrorAsync("23001", async () =>
        {
            await using var context = NewContext();
            await context.Questions.Where(q => q.Id == questionId).ExecuteDeleteAsync();
        });
    }

    [Fact]
    public async Task An_unused_question_can_be_deleted()
    {
        var questionId = await SeedQuestionAsync(await SeedChapterAsync(await SeedSubjectAsync()));

        await using var context = NewContext();
        var deleted = await context.Questions.Where(q => q.Id == questionId).ExecuteDeleteAsync();

        Assert.Equal(1, deleted);
    }

    [Fact]
    public async Task Deleting_a_paper_deletes_its_paper_questions_but_not_the_questions()
    {
        var subjectId = await SeedSubjectAsync();
        var questionId = await SeedQuestionAsync(await SeedChapterAsync(subjectId));
        var paperId = await SeedPaperAsync(subjectId);
        await AddToPaperAsync(paperId, questionId, orderIndex: 1);

        await using (var context = NewContext())
        {
            await context.Papers.Where(p => p.Id == paperId).ExecuteDeleteAsync();
        }

        await using var check = NewContext();
        Assert.Empty(await check.PaperQuestions.Where(pq => pq.PaperId == paperId).ToListAsync());
        Assert.True(await check.Questions.AnyAsync(q => q.Id == questionId));
    }
}
