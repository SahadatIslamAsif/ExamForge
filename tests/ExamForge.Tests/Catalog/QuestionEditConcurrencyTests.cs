using ExamForge.Domain.Catalog;
using ExamForge.Domain.Generation;
using ExamForge.Infrastructure.Catalog;
using ExamForge.Infrastructure.Persistence;
using ExamForge.Tests.GenerationService;
using ExamForge.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamForge.Tests.Catalog;

// A PATCH that edits a question and a generation that uses it must not interleave into a paper
// whose stored marks no longer add up. Each test runs one operation to completion inside the
// other's SaveChanges (the SavingChanges hook fires after loading and checking, before saving).
[Collection(PostgresCollection.Name)]
public class QuestionEditConcurrencyTests(PostgresFixture postgres) : GenerationTestBase(postgres)
{
    private QuestionService NewQuestionService(Action<ExamForgeDbContext>? onSaving = null) =>
        new(new TestDbContextFactory(Database, onSaving), Clock);

    // Runs the action on the first SaveChanges only, and counts every SaveChanges.
    private static (Func<int> Saves, Action<ExamForgeDbContext> Hook) OnFirstSave(Action action)
    {
        var saves = 0;
        return (() => saves, _ =>
        {
            if (Interlocked.Increment(ref saves) == 1)
            {
                action();
            }
        });
    }

    [Fact]
    public async Task A_paper_committed_between_the_usage_check_and_the_save_turns_the_edit_into_question_in_use()
    {
        // Q is the only question, so the generation must use it.
        var bank = await SeedBankAsync([5]);
        var q = bank.QuestionIds[0][0];

        GenerationResult? generated = null;
        var (saves, hook) = OnFirstSave(() =>
            generated = NewService().GenerateAsync(Spec(bank, [5])).GetAwaiter().GetResult());

        var result = await NewQuestionService(hook).UpdateAsync(q, new QuestionChanges(null, null, 3, null, null, null));

        var paper = AssertSuccess(generated!);
        Assert.Equal(CatalogErrors.QuestionInUse, Assert.IsType<CatalogResult<QuestionInfo>.Conflict>(result).Error);
        Assert.Equal(1, saves()); // the retry found the paper and refused before saving
        await using var context = NewContext();
        Assert.Equal(5, (await context.Questions.AsNoTracking().SingleAsync(x => x.Id == q)).Marks);
        Assert.Equal(paper.TotalMarks, await StoredMarksAsync(paper.Id));
    }

    [Fact]
    public async Task An_edit_committed_before_the_generation_saves_makes_the_generation_retry_with_the_new_marks()
    {
        // Q (5) and R (2). Before the edit only {Q} makes 5; after Q becomes 3 only {Q, R} does.
        var bank = await SeedBankAsync([5, 2]);
        var q = bank.QuestionIds[0][0];

        CatalogResult<QuestionInfo>? edited = null;
        var (saves, hook) = OnFirstSave(() =>
            edited = NewQuestionService().UpdateAsync(q, new QuestionChanges(null, null, 3, null, null, null)).GetAwaiter().GetResult());

        var paper = AssertSuccess(await NewService(onSaving: hook).GenerateAsync(Spec(bank, [5])));

        Assert.Equal(3, Assert.IsType<CatalogResult<QuestionInfo>.Ok>(edited!).Value.Marks);
        Assert.Equal(2, saves()); // first save conflicted, the retry saved
        Assert.Equal(bank.QuestionIds[0].Order(), Ids(paper).Order());
        Assert.Equal(5, await StoredMarksAsync(paper.Id));
        Assert.Equal(paper.TotalMarks, await StoredMarksAsync(paper.Id));
    }

    [Fact]
    public async Task Deactivating_a_question_never_conflicts_with_a_generation_in_either_order()
    {
        var bank = await SeedBankAsync([5, 2, 3]);
        var deactivate = new QuestionChanges(null, null, null, null, null, false);

        // Generation commits inside the PATCH's save: the PATCH must not need a retry.
        var (patchSaves, patchHook) = OnFirstSave(() =>
            AssertSuccess(NewService().GenerateAsync(Spec(bank, [5])).GetAwaiter().GetResult()));
        var patched = await NewQuestionService(patchHook).UpdateAsync(bank.QuestionIds[0][1], deactivate);
        Assert.IsType<CatalogResult<QuestionInfo>.Ok>(patched);
        Assert.Equal(1, patchSaves());

        // PATCH commits inside the generation's save: the generation must not need a retry.
        var (generationSaves, generationHook) = OnFirstSave(() =>
            Assert.IsType<CatalogResult<QuestionInfo>.Ok>(
                NewQuestionService().UpdateAsync(bank.QuestionIds[0][2], deactivate).GetAwaiter().GetResult()));
        AssertSuccess(await NewService(onSaving: generationHook).GenerateAsync(Spec(bank, [5])));
        Assert.Equal(1, generationSaves());
    }

    [Fact]
    public async Task An_edit_that_conflicts_on_every_attempt_is_concurrent_edit_after_three_attempts()
    {
        var bank = await SeedBankAsync([5]);
        var q = bank.QuestionIds[0][0];
        var saves = 0;

        var result = await NewQuestionService(_ =>
        {
            Interlocked.Increment(ref saves);
            TouchSubject(bank.SubjectId);
        }).UpdateAsync(q, new QuestionChanges(null, "Rewritten", null, null, null, null));

        Assert.Equal(CatalogErrors.ConcurrentEdit, Assert.IsType<CatalogResult<QuestionInfo>.Conflict>(result).Error);
        Assert.Equal(3, saves);
        await using var context = NewContext();
        Assert.NotEqual("Rewritten", (await context.Questions.AsNoTracking().SingleAsync(x => x.Id == q)).Text);
    }

    // The paper's marks as stored now: its lines joined to the questions' current marks.
    private async Task<int> StoredMarksAsync(int paperId)
    {
        await using var context = NewContext();
        return await context.PaperQuestions.Where(pq => pq.PaperId == paperId).SumAsync(pq => pq.Question.Marks);
    }

    private void TouchSubject(int subjectId)
    {
        using var connection = new NpgsqlConnection(Database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand("""UPDATE "Subjects" SET "Name" = "Name" WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", subjectId);
        command.ExecuteNonQuery();
    }
}
