using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExamForge.Domain.Generation;
using ExamForge.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ExamForge.Tests.Api;

[Collection(PostgresCollection.Name)]
public class GeneratePaperApiTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static readonly int[] ChapterBank = [1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 1, 2, 3];

    [Fact]
    public async Task Generate_returns_201_with_the_spec_body_and_a_location_that_returns_the_same_body()
    {
        var bank = await SeedBankAsync(ChapterBank, ChapterBank, ChapterBank);

        var response = await PostGenerateAsync(Request(bank, [20, 20, 10], seed: 20261018));
        var body = await JsonAsync(response, HttpStatusCode.Created);

        Assert.Equal(
            ["achievedDifficultyMix", "createdAt", "id", "questions", "seed", "subjectId", "title", "totalMarks"],
            Keys(body));
        Assert.Equal(bank.SubjectId, (int)body["subjectId"]!);
        Assert.Equal("Physics Practice — Week 6", (string)body["title"]!);
        Assert.Equal(50, (int)body["totalMarks"]!);
        Assert.Equal(20261018, (long)body["seed"]!);

        var mix = body["achievedDifficultyMix"]!;
        Assert.Equal(["easy", "hard", "medium"], Keys(mix));
        Assert.Equal(1.0, (double)mix["easy"]! + (double)mix["medium"]! + (double)mix["hard"]!, precision: 9);

        var createdAt = (string)body["createdAt"]!;
        Assert.EndsWith("Z", createdAt);

        var questions = body["questions"]!.AsArray();
        Assert.Equal(Enumerable.Range(1, questions.Count), questions.Select(q => (int)q!["orderIndex"]!));
        Assert.Equal(50, questions.Sum(q => (int)q!["marks"]!));
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(
                new[] { 20, 20, 10 }[i],
                questions.Where(q => (int)q!["chapterId"]! == bank.ChapterIds[i]).Sum(q => (int)q!["marks"]!));
        }

        Assert.All(questions, q =>
        {
            Assert.Equal(["chapterId", "chapterName", "difficulty", "marks", "orderIndex", "questionId", "text"], Keys(q!));
            Assert.Equal(JsonValueKind.String, q!["difficulty"]!.GetValueKind());
            Assert.Contains((string)q["difficulty"]!, new[] { "Easy", "Medium", "Hard" });
            Assert.False(string.IsNullOrWhiteSpace((string)q["text"]!));
            Assert.StartsWith("Chapter ", (string)q["chapterName"]!);
        });

        var location = response.Headers.Location!;
        Assert.Equal($"/api/papers/{(int)body["id"]!}", location.AbsolutePath);
        var fetched = await JsonAsync(await Client.GetAsync(location), HttpStatusCode.OK);
        Assert.True(JsonNode.DeepEquals(body, fetched), $"POST body:\n{body}\nGET body:\n{fetched}");
    }

    [Fact]
    public async Task An_omitted_seed_is_safe_for_javascript_and_reproduces_the_paper()
    {
        var bank = await SeedBankAsync(ChapterBank, ChapterBank);

        var first = await JsonAsync(await PostGenerateAsync(Request(bank, [15, 10], seed: null)), HttpStatusCode.Created);
        var seed = (long)first["seed"]!;
        Assert.InRange(seed, 0, SeedRange.MaxSafeInteger);

        var replay = await JsonAsync(await PostGenerateAsync(Request(bank, [15, 10], seed: seed)), HttpStatusCode.Created);

        Assert.Equal(QuestionIds(first), QuestionIds(replay));
    }

    [Fact]
    public async Task Generate_for_an_unknown_subject_is_404()
    {
        var bank = await SeedBankAsync([5]);
        var request = Request(bank, [5]);
        request["subjectId"] = 999_999;

        var response = await PostGenerateAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_paper_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/papers/999999")).StatusCode);
    }

    [Fact]
    public async Task A_conflict_is_409_with_the_concurrent_generation_error()
    {
        var bank = await SeedBankAsync([5]);
        var client = CreateClient(services =>
            services.AddScoped<IPaperGenerationService, AlwaysConflictingGenerationService>());

        var body = await JsonAsync(await PostGenerateAsync(Request(bank, [5]), client), HttpStatusCode.Conflict);

        Assert.Equal(["error", "message"], Keys(body));
        Assert.Equal("CONCURRENT_GENERATION", (string)body["error"]!);
        Assert.False(string.IsNullOrWhiteSpace((string)body["message"]!));
    }

    private sealed class AlwaysConflictingGenerationService : IPaperGenerationService
    {
        public Task<GenerationResult> GenerateAsync(PaperSpecification specification, CancellationToken cancellationToken = default) =>
            Task.FromResult<GenerationResult>(new GenerationResult.Conflict(3));
    }
}
