using System.Net;
using System.Text.Json.Nodes;
using ExamForge.Domain.Catalog;
using ExamForge.Tests.Persistence;

namespace ExamForge.Tests.Api;

[Collection(PostgresCollection.Name)]
public class ChaptersApiTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static JsonObject Chapter(int subjectId, int orderIndex, string name = "Forces") =>
        new() { ["subjectId"] = subjectId, ["name"] = name, ["orderIndex"] = orderIndex };

    [Fact]
    public async Task A_created_chapter_can_be_read_back_from_its_location()
    {
        var subjectId = await SeedSubjectAsync();

        var response = await PostAsync("/api/chapters", Chapter(subjectId, 1));
        var created = await JsonAsync(response, HttpStatusCode.Created);

        Assert.Equal(["id", "name", "orderIndex", "subjectId"], Keys(created));
        Assert.Equal(subjectId, (int)created["subjectId"]!);
        Assert.Equal($"/api/chapters/{(int)created["id"]!}", response.Headers.Location!.AbsolutePath);
        var fetched = await JsonAsync(await Client.GetAsync(response.Headers.Location), HttpStatusCode.OK);
        Assert.True(JsonNode.DeepEquals(created, fetched));
    }

    [Fact]
    public async Task Chapters_are_listed_by_order_index_for_their_subject_only()
    {
        var subjectId = await SeedSubjectAsync();
        var otherSubjectId = await SeedSubjectAsync();
        foreach (var orderIndex in new[] { 3, 1, 2 })
        {
            await JsonAsync(await PostAsync("/api/chapters", Chapter(subjectId, orderIndex)), HttpStatusCode.Created);
        }

        await JsonAsync(await PostAsync("/api/chapters", Chapter(otherSubjectId, 1)), HttpStatusCode.Created);

        var listed = (await JsonAsync(await Client.GetAsync($"/api/chapters?subjectId={subjectId}"), HttpStatusCode.OK)).AsArray();

        Assert.Equal([1, 2, 3], listed.Select(c => (int)c!["orderIndex"]!));
        Assert.All(listed, c => Assert.Equal(subjectId, (int)c!["subjectId"]!));
    }

    [Fact]
    public async Task A_duplicate_order_index_in_a_subject_is_409()
    {
        var subjectId = await SeedSubjectAsync();
        await JsonAsync(await PostAsync("/api/chapters", Chapter(subjectId, 1)), HttpStatusCode.Created);

        await AssertConflictAsync(await PostAsync("/api/chapters", Chapter(subjectId, 1, "Again")), CatalogErrors.ChapterOrderTaken);
    }

    [Fact]
    public async Task Listing_needs_a_known_subject()
    {
        await AssertFieldErrorAsync(await Client.GetAsync("/api/chapters"), "subjectId");
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/chapters?subjectId=999999")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_chapter_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/chapters/999999")).StatusCode);
    }

    [Theory]
    [InlineData("subjectId missing", "subjectId")]
    [InlineData("subject unknown", "subjectId")]
    [InlineData("name missing", "name")]
    [InlineData("name blank", "name")]
    [InlineData("name too long", "name")]
    [InlineData("orderIndex missing", "orderIndex")]
    [InlineData("orderIndex below 1", "orderIndex")]
    public async Task An_invalid_chapter_is_400_naming_the_field(string rule, string field)
    {
        var body = Chapter(await SeedSubjectAsync(), 1);
        switch (rule)
        {
            case "subjectId missing": body.Remove("subjectId"); break;
            case "subject unknown": body["subjectId"] = 999_999; break;
            case "name missing": body.Remove("name"); break;
            case "name blank": body["name"] = "  "; break;
            case "name too long": body["name"] = new string('x', 201); break;
            case "orderIndex missing": body.Remove("orderIndex"); break;
            case "orderIndex below 1": body["orderIndex"] = 0; break;
        }

        await AssertFieldErrorAsync(await PostAsync("/api/chapters", body), field);
    }
}
