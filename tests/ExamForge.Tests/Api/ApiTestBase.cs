using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ExamForge.Tests.GenerationService;
using ExamForge.Tests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ExamForge.Tests.Api;

// Boots the real Program in memory, pointed at this test class's database and the fake clock.
public sealed class ExamForgeApiFactory(
    string connectionString,
    TimeProvider clock,
    Action<IServiceCollection>? configureServices = null,
    string environment = "Development") : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:ExamForge", connectionString);

        // Runs after Program's own registrations, so these replace them.
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(clock);
            configureServices?.Invoke(services);
        });
    }
}

public abstract class ApiTestBase(PostgresFixture postgres) : GenerationTestBase(postgres)
{
    protected const string GenerateUrl = "/api/papers/generate";

    private readonly List<IDisposable> _disposables = [];
    private HttpClient? _client;

    protected HttpClient Client => _client ??= CreateClient();

    protected HttpClient CreateClient(Action<IServiceCollection>? configureServices = null, string environment = "Development")
    {
        var factory = new ExamForgeApiFactory(Database.ConnectionString, Clock, configureServices, environment);
        var client = factory.CreateClient();
        _disposables.Add(client);
        _disposables.Add(factory);
        return client;
    }

    public override Task DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        return base.DisposeAsync();
    }

    // A valid request for the bank, as raw JSON so tests can break it in any way.
    protected static JsonObject Request(
        Bank bank,
        int[] marksPerChapter,
        long? seed = 42,
        int excludeLastNPapers = 0,
        string title = "Physics Practice — Week 6")
    {
        var request = new JsonObject
        {
            ["subjectId"] = bank.SubjectId,
            ["title"] = title,
            ["totalMarks"] = marksPerChapter.Sum(),
            ["chapterDistribution"] = new JsonArray(bank.ChapterIds
                .Zip(marksPerChapter, (id, marks) => (JsonNode)new JsonObject { ["chapterId"] = id, ["marks"] = marks })
                .ToArray()),
            ["difficultyMix"] = new JsonObject { ["easy"] = 0.3, ["medium"] = 0.5, ["hard"] = 0.2 },
            ["excludeLastNPapers"] = excludeLastNPapers,
        };
        if (seed is not null)
        {
            request["seed"] = seed;
        }

        return request;
    }

    protected Task<HttpResponseMessage> PostGenerateAsync(JsonNode request, HttpClient? client = null) =>
        (client ?? Client).PostAsync(GenerateUrl, JsonBody(request));

    protected Task<HttpResponseMessage> PostAsync(string url, JsonNode body) => Client.PostAsync(url, JsonBody(body));

    protected Task<HttpResponseMessage> PatchAsync(string url, JsonNode body) => Client.PatchAsync(url, JsonBody(body));

    private static StringContent JsonBody(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    // A 400 ValidationProblemDetails with at least one message for the field.
    protected static async Task AssertFieldErrorAsync(HttpResponseMessage response, string field)
    {
        var body = await JsonAsync(response, HttpStatusCode.BadRequest);
        var errors = body["errors"]!.AsObject();
        Assert.True(errors.ContainsKey(field), $"Expected an error for '{field}', got: {errors.ToJsonString()}");
        Assert.NotEmpty(errors[field]!.AsArray());
    }

    // A 409 with the {error, message} body; returns the message.
    protected static async Task<string> AssertConflictAsync(HttpResponseMessage response, string error)
    {
        var body = await JsonAsync(response, HttpStatusCode.Conflict);
        Assert.Equal(["error", "message"], Keys(body));
        Assert.Equal(error, (string)body["error"]!);
        return (string)body["message"]!;
    }

    protected static async Task<JsonNode> JsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!;
    }

    protected static IEnumerable<string> Keys(JsonNode node) => node.AsObject().Select(p => p.Key).Order();

    protected static List<int> QuestionIds(JsonNode paper) =>
        paper["questions"]!.AsArray().Select(q => (int)q!["questionId"]!).ToList();
}
