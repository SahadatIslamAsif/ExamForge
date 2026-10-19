using System.Text.Json.Nodes;
using ExamForge.Tests.Persistence;
using Xunit.Abstractions;

namespace ExamForge.Tests.EndToEnd;

// The same body with the seed a paper reports must give the same questions in the same order.
// excludeLastNPapers is 0, so the papers generated in between cannot change the eligible pool.
[Collection(PostgresCollection.Name)]
public class DeterminismOverHttpTests(PostgresFixture postgres, ITestOutputHelper output) : EndToEndTestBase(postgres)
{
    [Fact]
    public async Task Twenty_papers_replayed_with_their_seed_come_back_identical()
    {
        var subject = await SeedRealBankAsync();
        var random = new Random(4242);
        var compared = 0;
        var attempts = 0;
        var mismatches = new List<string>();

        while (compared < 20)
        {
            Assert.True(++attempts <= 200, $"Only {compared} successful specs in 200 attempts.");

            var spec = NextSpec(random, subject, excludeLastNPapers: 0);
            var (status, first, _) = await ReadAsync(await PostGenerateAsync(spec.Body));
            if (status != 201)
            {
                continue;
            }

            var replayBody = (JsonObject)spec.Body.DeepClone();
            replayBody["seed"] = (long)first!["seed"]!;
            var (replayStatus, replay, replayText) = await ReadAsync(await PostGenerateAsync(replayBody));

            Assert.True(replayStatus == 201, $"Replay of {replayBody.ToJsonString()} returned {replayStatus}: {replayText}");
            if (!QuestionIds(first).SequenceEqual(QuestionIds(replay!)))
            {
                mismatches.Add($"{replayBody.ToJsonString()}: [{string.Join(",", QuestionIds(first))}] then [{string.Join(",", QuestionIds(replay!))}]");
            }

            compared++;
        }

        output.WriteLine($"{compared} papers replayed from {attempts} random specs; {mismatches.Count} mismatches.");
        Assert.Empty(mismatches);
    }
}
