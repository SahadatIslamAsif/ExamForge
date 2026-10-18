using System.Net;
using System.Text.Json.Nodes;
using ExamForge.Tests.Persistence;

namespace ExamForge.Tests.Api;

[Collection(PostgresCollection.Name)]
public class InfeasibleApiTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Fact]
    public async Task A_shortfall_caused_by_the_history_window_says_so()
    {
        // 18 marks in the chapter; three 5-mark questions are in the last 3 papers, leaving 3.
        var bank = await SeedBankAsync([5, 5, 5, 3]);
        var q = bank.QuestionIds[0];
        await SeedHistoryPaperAsync(bank.SubjectId, Day(1), q[0]);
        await SeedHistoryPaperAsync(bank.SubjectId, Day(2), q[1]);
        await SeedHistoryPaperAsync(bank.SubjectId, Day(3), q[2]);

        var failure = Assert.Single(await PostInfeasibleAsync(Request(bank, [10], excludeLastNPapers: 3)));

        Assert.Equal("chapterDistribution", (string)failure["constraint"]!);
        Assert.Equal(10, (int)failure["requestedMarks"]!);
        Assert.Equal(3, (int)failure["availableMarks"]!);
        Assert.Equal("7 marks short after excluding questions used in the last 3 papers.", (string)failure["detail"]!);
        Assert.Equal(3, await PaperCountAsync(bank.SubjectId)); // only the history papers
    }

    [Fact]
    public async Task A_shortfall_without_the_window_reports_the_active_marks()
    {
        var bank = await SeedBankAsync([1, 2, 3]);

        var failure = Assert.Single(await PostInfeasibleAsync(Request(bank, [10])));

        Assert.Equal("chapterDistribution", (string)failure["constraint"]!);
        Assert.Equal(bank.ChapterIds[0], (int)failure["chapterId"]!);
        Assert.Equal("Chapter 1", (string)failure["chapterName"]!);
        Assert.Equal(6, (int)failure["availableMarks"]!);
        Assert.Equal("4 marks short: the chapter's active questions total 6 marks.", (string)failure["detail"]!);
        Assert.Equal(0, await PaperCountAsync(bank.SubjectId));
    }

    [Fact]
    public async Task No_exact_combination_is_an_exact_marks_failure()
    {
        var bank = await SeedBankAsync([2, 4, 6]);

        var failure = Assert.Single(await PostInfeasibleAsync(Request(bank, [9])));

        Assert.Equal("exactMarks", (string)failure["constraint"]!);
        Assert.Equal(9, (int)failure["requestedMarks"]!);
        Assert.Equal(12, (int)failure["availableMarks"]!);
        Assert.Equal("Eligible questions total 12 marks, but no combination sums to exactly 9.", (string)failure["detail"]!);
        Assert.Equal(0, await PaperCountAsync(bank.SubjectId));
    }

    [Fact]
    public async Task Two_failing_chapters_are_reported_together()
    {
        var bank = await SeedBankAsync([1, 2], [2, 4, 6], [5, 5]);

        var failures = await PostInfeasibleAsync(Request(bank, [10, 9, 5]));

        Assert.Equal(2, failures.Count);
        Assert.Equal(bank.ChapterIds[0], (int)failures[0]["chapterId"]!);
        Assert.Equal("chapterDistribution", (string)failures[0]["constraint"]!);
        Assert.Equal(bank.ChapterIds[1], (int)failures[1]["chapterId"]!);
        Assert.Equal("exactMarks", (string)failures[1]["constraint"]!);
        Assert.Equal(0, await PaperCountAsync(bank.SubjectId));
    }

    private async Task<List<JsonNode>> PostInfeasibleAsync(JsonObject request)
    {
        var body = await JsonAsync(await PostGenerateAsync(request), HttpStatusCode.UnprocessableEntity);

        Assert.Equal(["error", "failures", "message"], Keys(body));
        Assert.Equal("INFEASIBLE_SPECIFICATION", (string)body["error"]!);
        Assert.Equal("Cannot satisfy the requested mark distribution.", (string)body["message"]!);

        var failures = body["failures"]!.AsArray().Select(f => f!).ToList();
        Assert.All(failures, f => Assert.Equal(
            ["availableMarks", "chapterId", "chapterName", "constraint", "detail", "requestedMarks"], Keys(f)));
        return failures;
    }
}
