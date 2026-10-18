using System.Net;
using System.Text.Json.Nodes;
using ExamForge.Domain.Catalog;
using ExamForge.Domain.Entities;
using ExamForge.Tests.GenerationService;
using ExamForge.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Tests.Api;

// A question that appears in a paper is frozen except for isActive: GET /api/papers/{id} reads
// the question's current text, marks and difficulty, so an edit would rewrite old papers.
[Collection(PostgresCollection.Name)]
public class QuestionInUseApiTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    // Two chapters; the paper uses the first 5-mark question of chapter 1.
    private async Task<(Bank Bank, int Used, int Spare, int PaperId, string PaperBefore)> UsedQuestionAsync()
    {
        var bank = await SeedBankAsync([5, 5], [5]);
        var used = bank.QuestionIds[0][0];
        var paperId = await SeedHistoryPaperAsync(bank.SubjectId, Day(1), used);
        var paperBefore = await (await Client.GetAsync($"/api/papers/{paperId}")).Content.ReadAsStringAsync();
        return (bank, used, bank.QuestionIds[0][1], paperId, paperBefore);
    }

    private async Task<Question> RowAsync(int id)
    {
        await using var context = NewContext();
        return await context.Questions.AsNoTracking().SingleAsync(q => q.Id == id);
    }

    private async Task AssertPaperUnchangedAsync(int paperId, string before) =>
        Assert.Equal(before, await (await Client.GetAsync($"/api/papers/{paperId}")).Content.ReadAsStringAsync());

    [Theory]
    [InlineData("marks")]
    [InlineData("chapterId")]
    [InlineData("text")]
    [InlineData("difficulty")]
    [InlineData("type")]
    public async Task Changing_a_used_question_is_409_and_leaves_the_row_and_paper_unchanged(string field)
    {
        var (bank, used, _, paperId, paperBefore) = await UsedQuestionAsync();
        var before = await RowAsync(used);
        var patch = field switch
        {
            "marks" => new JsonObject { ["marks"] = 4 },
            "chapterId" => new JsonObject { ["chapterId"] = bank.ChapterIds[1] },
            "text" => new JsonObject { ["text"] = "Rewritten" },
            "difficulty" => new JsonObject { ["difficulty"] = "Hard" }, // seeded as Easy
            "type" => new JsonObject { ["type"] = "Mcq" },               // seeded as Short
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        var message = await AssertConflictAsync(await PatchAsync($"/api/questions/{used}", patch), CatalogErrors.QuestionInUse);

        Assert.Contains(field, message);
        var after = await RowAsync(used);
        Assert.Equal(
            (before.ChapterId, before.Text, before.Marks, before.Difficulty, before.Type, before.IsActive),
            (after.ChapterId, after.Text, after.Marks, after.Difficulty, after.Type, after.IsActive));
        await AssertPaperUnchangedAsync(paperId, paperBefore);
    }

    [Fact]
    public async Task The_conflict_names_every_changed_field()
    {
        var (_, used, _, _, _) = await UsedQuestionAsync();

        var message = await AssertConflictAsync(
            await PatchAsync($"/api/questions/{used}", new JsonObject { ["text"] = "New", ["marks"] = 1, ["isActive"] = false }),
            CatalogErrors.QuestionInUse);

        Assert.Contains("text", message);
        Assert.Contains("marks", message);
        // The whole request is rejected, including the isActive part of it.
        Assert.True((await RowAsync(used)).IsActive);
    }

    [Fact]
    public async Task Deactivating_a_used_question_works_and_generation_never_selects_it_again()
    {
        var (bank, used, spare, paperId, paperBefore) = await UsedQuestionAsync();
        var row = await RowAsync(used);

        // Fields sent with their current values are not changes, so only isActive changes here.
        var patched = await JsonAsync(
            await PatchAsync($"/api/questions/{used}", new JsonObject
            {
                ["text"] = row.Text,
                ["marks"] = row.Marks,
                ["difficulty"] = row.Difficulty.ToString(),
                ["isActive"] = false,
            }),
            HttpStatusCode.OK);

        Assert.False((bool)patched["isActive"]!);
        Assert.Equal(1, (int)patched["usedInPapers"]!);
        await AssertPaperUnchangedAsync(paperId, paperBefore);

        // Chapter 1 now has only the spare 5-mark question active, and no history exclusion applies.
        var paper = await JsonAsync(await PostGenerateAsync(Request(bank, [5, 5], excludeLastNPapers: 0)), HttpStatusCode.Created);
        Assert.Contains(spare, QuestionIds(paper));
        Assert.DoesNotContain(used, QuestionIds(paper));

        var infeasible = await JsonAsync(await PostGenerateAsync(Request(bank, [10, 5], excludeLastNPapers: 0)), HttpStatusCode.UnprocessableEntity);
        Assert.Equal(5, (int)infeasible["failures"]![0]!["availableMarks"]!);
    }

    [Fact]
    public async Task Deleting_a_used_question_is_409_and_the_row_stays()
    {
        var (_, used, _, paperId, paperBefore) = await UsedQuestionAsync();

        var message = await AssertConflictAsync(await Client.DeleteAsync($"/api/questions/{used}"), CatalogErrors.QuestionInUse);

        Assert.Contains("isActive", message);
        await using (var context = NewContext())
        {
            Assert.True(await context.Questions.AnyAsync(q => q.Id == used));
        }

        await AssertPaperUnchangedAsync(paperId, paperBefore);
    }

    [Fact]
    public async Task A_used_question_reports_how_many_papers_use_it()
    {
        var (bank, used, _, _, _) = await UsedQuestionAsync();
        await SeedHistoryPaperAsync(bank.SubjectId, Day(2), used);

        var question = await JsonAsync(await Client.GetAsync($"/api/questions/{used}"), HttpStatusCode.OK);

        Assert.Equal(2, (int)question["usedInPapers"]!);
    }
}
