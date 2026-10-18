using System.Net;
using System.Text.Json.Nodes;
using ExamForge.Tests.Persistence;

namespace ExamForge.Tests.Api;

[Collection(PostgresCollection.Name)]
public class SubjectsApiTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Fact]
    public async Task A_created_subject_can_be_read_back_from_its_location()
    {
        var response = await PostAsync("/api/subjects", new JsonObject { ["name"] = "Physics", ["level"] = "O Level" });
        var created = await JsonAsync(response, HttpStatusCode.Created);

        Assert.Equal(["id", "level", "name"], Keys(created));
        Assert.Equal("Physics", (string)created["name"]!);
        Assert.Equal("O Level", (string)created["level"]!);
        Assert.Equal($"/api/subjects/{(int)created["id"]!}", response.Headers.Location!.AbsolutePath);

        var fetched = await JsonAsync(await Client.GetAsync(response.Headers.Location), HttpStatusCode.OK);
        Assert.True(JsonNode.DeepEquals(created, fetched));
    }

    [Fact]
    public async Task Subjects_are_listed_by_id()
    {
        var first = await JsonAsync(await PostAsync("/api/subjects", new JsonObject { ["name"] = "B", ["level"] = "L" }), HttpStatusCode.Created);
        var second = await JsonAsync(await PostAsync("/api/subjects", new JsonObject { ["name"] = "A", ["level"] = "L" }), HttpStatusCode.Created);

        var ids = (await JsonAsync(await Client.GetAsync("/api/subjects"), HttpStatusCode.OK))
            .AsArray().Select(s => (int)s!["id"]!).ToList();

        Assert.Equal(ids.Order(), ids);
        Assert.Contains((int)first["id"]!, ids);
        Assert.Contains((int)second["id"]!, ids);
    }

    [Fact]
    public async Task An_unknown_subject_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/subjects/999999")).StatusCode);
    }

    [Theory]
    [InlineData("name missing", "name")]
    [InlineData("name blank", "name")]
    [InlineData("name too long", "name")]
    [InlineData("level missing", "level")]
    [InlineData("level blank", "level")]
    [InlineData("level too long", "level")]
    public async Task An_invalid_subject_is_400_naming_the_field(string rule, string field)
    {
        var body = new JsonObject { ["name"] = "Physics", ["level"] = "O Level" };
        switch (rule)
        {
            case "name missing": body.Remove("name"); break;
            case "name blank": body["name"] = " "; break;
            case "name too long": body["name"] = new string('x', 201); break;
            case "level missing": body.Remove("level"); break;
            case "level blank": body["level"] = ""; break;
            case "level too long": body["level"] = new string('x', 101); break;
        }

        await AssertFieldErrorAsync(await PostAsync("/api/subjects", body), field);
    }
}
