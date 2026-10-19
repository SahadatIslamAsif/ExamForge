using System.Net;
using System.Text.Json.Nodes;
using ExamForge.Tests.EndToEnd;
using ExamForge.Tests.Persistence;

namespace ExamForge.Tests.Seeding;

// What the seeded bank can and cannot sustain. Each chapter holds 29 marks (1,1,1,2,2,2,2,3,3,3,4,5).
// With excludeLastNPapers = N, a paper may only use questions outside the previous N papers, so
// every N+1 consecutive papers need disjoint questions: a chapter can supply about 29 / (N+1) marks
// per paper. For N = 3 that is about 7 marks, so 5 marks per chapter is sustainable and 15 is not.

[Collection(PostgresCollection.Name)]
public class SeededBankCapacityTests(PostgresFixture postgres) : EndToEndTestBase(postgres)
{
    [Fact]
    public async Task Six_thirty_mark_papers_over_all_six_chapters_succeed_with_a_three_paper_window()
    {
        var subject = await SeedRealBankAsync();
        var body = Spec(subject, subject.Chapters.OrderBy(c => c.OrderIndex).Select(c => (c.Id, 5)).ToList());

        for (var paper = 1; paper <= 6; paper++)
        {
            var previousThree = await HistoryAsync(subject.Id, 3);
            var (status, json, text) = await ReadAsync(await PostGenerateAsync(body));

            Assert.True(status == 201, $"Paper {paper} returned {status}: {text}");
            var shared = QuestionIds(json!).Where(previousThree.Contains).ToList();
            Assert.True(shared.Count == 0, $"Paper {paper} reuses {string.Join(",", shared)} from the previous 3 papers.");
            Assert.Equal(30, json!["questions"]!.AsArray().Sum(q => (int)q!["marks"]!));
        }
    }

    internal static JsonObject Spec(BankSubject subject, IReadOnlyList<(int ChapterId, int Marks)> distribution) => new()
    {
        ["subjectId"] = subject.Id,
        ["title"] = "Weekly practice",
        ["totalMarks"] = distribution.Sum(d => d.Marks),
        ["chapterDistribution"] = new JsonArray(distribution
            .Select(d => (JsonNode)new JsonObject { ["chapterId"] = d.ChapterId, ["marks"] = d.Marks })
            .ToArray()),
        ["difficultyMix"] = new JsonObject { ["easy"] = 0.3, ["medium"] = 0.5, ["hard"] = 0.2 },
        ["excludeLastNPapers"] = 3,
    };
}

[Collection(PostgresCollection.Name)]
public class SeededBankLimitTests(PostgresFixture postgres) : EndToEndTestBase(postgres)
{
    // 15 marks from a 29-mark chapter leaves 14 eligible marks for the next paper: the bank honestly
    // reports the second paper as infeasible instead of returning a short one.
    [Fact]
    public async Task Fifteen_marks_per_chapter_with_a_three_paper_window_runs_out_at_the_second_paper()
    {
        var subject = await SeedRealBankAsync();
        var chapters = subject.Chapters.OrderBy(c => c.OrderIndex).Take(4).Select(c => c.Id).ToList();
        var body = SeededBankCapacityTests.Spec(subject, chapters.Zip([15, 15, 10, 10], (id, marks) => (id, marks)).ToList());

        var (firstStatus, _, firstText) = await ReadAsync(await PostGenerateAsync(body));
        Assert.True(firstStatus == 201, $"Paper 1 returned {firstStatus}: {firstText}");

        var second = await JsonAsync(await PostGenerateAsync(body), HttpStatusCode.UnprocessableEntity);
        var failures = second["failures"]!.AsArray().Select(f => f!).ToList();

        Assert.Equal(chapters.Take(2), failures.Select(f => (int)f["chapterId"]!));
        Assert.All(failures, failure =>
        {
            Assert.Equal("chapterDistribution", (string)failure["constraint"]!);
            Assert.Equal(15, (int)failure["requestedMarks"]!);
            Assert.Equal(14, (int)failure["availableMarks"]!);
            Assert.Equal("1 mark short after excluding questions used in the last 3 papers.", (string)failure["detail"]!);
        });
    }
}
