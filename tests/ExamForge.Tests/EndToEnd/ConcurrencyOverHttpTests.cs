using System.Text.Json.Nodes;
using ExamForge.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace ExamForge.Tests.EndToEnd;

// Ten identical generate requests at once, five times over. With excludeLastNPapers 1, every
// stored paper must be disjoint from the paper stored just before it, whatever the interleaving.
[Collection(PostgresCollection.Name)]
public class ConcurrencyOverHttpTests(PostgresFixture postgres, ITestOutputHelper output) : EndToEndTestBase(postgres)
{
    [Fact]
    public async Task Parallel_requests_end_in_201_or_409_and_consecutive_papers_never_share_a_question()
    {
        var subject = await SeedRealBankAsync();
        var chapters = subject.Chapters.OrderBy(c => c.OrderIndex).Take(2).ToList();

        // One fixed seed for every request: without the concurrency token, racing requests would
        // read the same history and pick exactly the same questions.
        var body = new JsonObject
        {
            ["subjectId"] = subject.Id,
            ["title"] = "Race",
            ["totalMarks"] = 12,
            ["chapterDistribution"] = new JsonArray(
                new JsonObject { ["chapterId"] = chapters[0].Id, ["marks"] = 6 },
                new JsonObject { ["chapterId"] = chapters[1].Id, ["marks"] = 6 }),
            ["difficultyMix"] = new JsonObject { ["easy"] = 0.3, ["medium"] = 0.5, ["hard"] = 0.2 },
            ["excludeLastNPapers"] = 1,
            ["seed"] = 42,
        };

        var problems = new List<string>();
        int created = 0, conflicts = 0;

        for (var round = 1; round <= 5; round++)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostGenerateAsync(body)));
            foreach (var response in responses)
            {
                var (status, json, text) = await ReadAsync(response);
                if (status == 201)
                {
                    created++;
                }
                else if (status == 409 && (string?)json?["error"] == "CONCURRENT_GENERATION")
                {
                    conflicts++;
                }
                else
                {
                    problems.Add($"round {round}: unexpected {status}: {text}");
                }
            }
        }

        // The stored papers, in the order the history query sees them, checked pairwise.
        await using var context = NewContext();
        var papers = await context.Papers.AsNoTracking()
            .Where(p => p.SubjectId == subject.Id)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => new { p.Id, Questions = p.PaperQuestions.Select(pq => pq.QuestionId).ToList() })
            .ToListAsync();

        if (papers.Count != created)
        {
            problems.Add($"{created} responses were 201 but {papers.Count} papers are stored");
        }

        for (var i = 1; i < papers.Count; i++)
        {
            var shared = papers[i].Questions.Intersect(papers[i - 1].Questions).ToList();
            if (shared.Count > 0)
            {
                problems.Add($"paper {papers[i].Id} shares {string.Join(",", shared)} with paper {papers[i - 1].Id}");
            }
        }

        output.WriteLine($"50 requests in 5 rounds of 10: {created} x 201, {conflicts} x 409; {papers.Count} papers stored.");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(10)));
        Assert.True(created >= 5, "At least one request per round should succeed.");
    }
}
