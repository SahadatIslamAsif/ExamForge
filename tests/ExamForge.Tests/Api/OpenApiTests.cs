using System.Net;
using System.Text.Json.Nodes;
using ExamForge.Tests.Persistence;

namespace ExamForge.Tests.Api;

// The OpenAPI document and Swagger UI are for local exploration: on in Development, off elsewhere.
[Collection(PostgresCollection.Name)]
public class OpenApiTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private async Task<JsonNode> DocumentAsync() =>
        await JsonAsync(await Client.GetAsync("/openapi/v1.json"), HttpStatusCode.OK);

    [Fact]
    public async Task The_generate_operation_documents_every_status_it_returns()
    {
        var responses = (await DocumentAsync())["paths"]!["/api/papers/generate"]!["post"]!["responses"]!.AsObject();

        Assert.Equal(["201", "400", "404", "409", "422"], responses.Select(r => r.Key).Order());
    }

    [Fact]
    public async Task Every_operation_documents_its_success_and_error_statuses()
    {
        var paths = (await DocumentAsync())["paths"]!.AsObject();

        // Spot checks across the controllers: each real status appears, not just a bare 200.
        Assert.True(paths["/api/papers/{id}"]!["get"]!["responses"]!.AsObject().ContainsKey("404"));
        Assert.True(paths["/api/chapters"]!["post"]!["responses"]!.AsObject().ContainsKey("409"));
        Assert.True(paths["/api/questions/{id}"]!["patch"]!["responses"]!.AsObject().ContainsKey("409"));
        Assert.True(paths["/api/questions/{id}"]!["delete"]!["responses"]!.AsObject().ContainsKey("204"));
        Assert.True(paths["/api/subjects"]!["post"]!["responses"]!.AsObject().ContainsKey("201"));
    }

    [Fact]
    public async Task Operations_only_consume_and_produce_json()
    {
        var paths = (await DocumentAsync())["paths"]!.AsObject();
        var operations = paths.SelectMany(path => path.Value!.AsObject()
            .Where(op => op.Value!["responses"] is not null)
            .Select(op => (Name: $"{op.Key.ToUpperInvariant()} {path.Key}", Operation: op.Value!)));

        foreach (var (name, operation) in operations)
        {
            if (operation["requestBody"]?["content"] is JsonObject requestContent)
            {
                Assert.True(requestContent.Select(c => c.Key).SequenceEqual(["application/json"]), $"{name} consumes {requestContent.ToJsonString()}");
            }

            foreach (var (status, response) in operation["responses"]!.AsObject())
            {
                if (response!["content"] is not JsonObject content)
                {
                    continue; // e.g. 204
                }

                // Errors are problem details (RFC 9457), everything else plain JSON.
                var expected = status is "400" or "404" ? "application/problem+json" : "application/json";
                Assert.True(content.Select(c => c.Key).SequenceEqual([expected]), $"{name} {status} produces {string.Join(", ", content.Select(c => c.Key))}");
            }
        }
    }

    [Fact]
    public async Task Errors_are_problem_json_and_non_json_bodies_are_rejected()
    {
        var invalid = await Client.PostAsync("/api/subjects", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType!.MediaType);

        var notFound = await Client.GetAsync("/api/subjects/999999");
        Assert.Equal("application/problem+json", notFound.Content.Headers.ContentType!.MediaType);

        var textJson = await Client.PostAsync(
            "/api/subjects", new StringContent("""{"name":"X","level":"Y"}""", System.Text.Encoding.UTF8, "text/json"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, textJson.StatusCode);
    }

    [Fact]
    public async Task Difficulty_is_a_string_enum_in_the_schema()
    {
        var difficulty = (await DocumentAsync())["components"]!["schemas"]!["Difficulty"]!;

        Assert.Equal(["Easy", "Medium", "Hard"], difficulty["enum"]!.AsArray().Select(v => (string)v!));
    }

    [Fact]
    public async Task The_generate_request_carries_the_example_from_the_spec()
    {
        var example = (await DocumentAsync())["components"]!["schemas"]!["GeneratePaperRequest"]!["examples"]![0]!;

        Assert.Equal("Physics Practice — Week 6", (string)example["title"]!);
        Assert.Equal(50, (int)example["totalMarks"]!);
        Assert.Equal(3, example["chapterDistribution"]!.AsArray().Count);
        Assert.Equal(3, (int)example["excludeLastNPapers"]!);
    }

    [Fact]
    public async Task Swagger_ui_is_served_in_development()
    {
        var response = await Client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Neither_swagger_nor_the_document_exists_outside_development()
    {
        var production = CreateClient(environment: "Production");

        Assert.Equal(HttpStatusCode.NotFound, (await production.GetAsync("/swagger")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await production.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await production.GetAsync("/openapi/v1.json")).StatusCode);
    }
}
