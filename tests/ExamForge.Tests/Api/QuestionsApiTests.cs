using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExamForge.Tests.Persistence;

namespace ExamForge.Tests.Api;

// Questions that no paper uses: create, read, list, edit and delete freely.
[Collection(PostgresCollection.Name)]
public class QuestionsApiTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static JsonObject Question(int chapterId) => new()
    {
        ["chapterId"] = chapterId,
        ["text"] = "State Newton's first law.",
        ["marks"] = 3,
        ["difficulty"] = "Hard",
        ["type"] = "Structured",
    };

    private async Task<JsonNode> CreateAsync(JsonObject body) =>
        await JsonAsync(await PostAsync("/api/questions", body), HttpStatusCode.Created);

    [Fact]
    public async Task A_created_question_is_active_by_default_and_can_be_read_back()
    {
        var chapterId = await SeedChapterAsync(await SeedSubjectAsync());

        var response = await PostAsync("/api/questions", Question(chapterId));
        var created = await JsonAsync(response, HttpStatusCode.Created);

        Assert.Equal(
            ["chapterId", "createdAt", "difficulty", "id", "isActive", "marks", "text", "type", "usedInPapers"],
            Keys(created));
        Assert.Equal(chapterId, (int)created["chapterId"]!);
        Assert.Equal("State Newton's first law.", (string)created["text"]!);
        Assert.Equal(3, (int)created["marks"]!);
        Assert.Equal(JsonValueKind.String, created["difficulty"]!.GetValueKind());
        Assert.Equal("Hard", (string)created["difficulty"]!);
        Assert.Equal("Structured", (string)created["type"]!);
        Assert.True((bool)created["isActive"]!);
        Assert.Equal(0, (int)created["usedInPapers"]!);
        Assert.EndsWith("Z", (string)created["createdAt"]!);

        Assert.Equal($"/api/questions/{(int)created["id"]!}", response.Headers.Location!.AbsolutePath);
        var fetched = await JsonAsync(await Client.GetAsync(response.Headers.Location), HttpStatusCode.OK);
        Assert.True(JsonNode.DeepEquals(created, fetched));
    }

    [Fact]
    public async Task IsActive_can_be_set_on_creation()
    {
        var chapterId = await SeedChapterAsync(await SeedSubjectAsync());
        var body = Question(chapterId);
        body["isActive"] = false;

        Assert.False((bool)(await CreateAsync(body))["isActive"]!);
    }

    [Fact]
    public async Task Questions_are_listed_by_id_with_optional_chapter_and_difficulty_filters()
    {
        var subjectId = await SeedSubjectAsync();
        var chapterA = await SeedChapterAsync(subjectId, orderIndex: 1);
        var chapterB = await SeedChapterAsync(subjectId, orderIndex: 2);
        var hardA = (int)(await CreateAsync(Question(chapterA)))["id"]!;
        var easy = Question(chapterA);
        easy["difficulty"] = "Easy";
        var easyA = (int)(await CreateAsync(easy))["id"]!;
        var hardB = (int)(await CreateAsync(Question(chapterB)))["id"]!;

        async Task<List<int>> List(string query) =>
            (await JsonAsync(await Client.GetAsync($"/api/questions{query}"), HttpStatusCode.OK))
            .AsArray().Select(q => (int)q!["id"]!).ToList();

        var all = await List("");
        Assert.Equal(all.Order(), all);
        Assert.Equal([hardA, easyA], await List($"?chapterId={chapterA}"));

        var hard = await List("?difficulty=Hard");
        Assert.Contains(hardA, hard);
        Assert.Contains(hardB, hard);
        Assert.DoesNotContain(easyA, hard);

        Assert.Equal([hardA], await List($"?chapterId={chapterA}&difficulty=Hard"));
    }

    [Fact]
    public async Task An_unknown_difficulty_filter_is_400()
    {
        await AssertFieldErrorAsync(await Client.GetAsync("/api/questions?difficulty=Hardd"), "difficulty");
    }

    [Fact]
    public async Task Unknown_questions_are_404_for_get_patch_and_delete()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/questions/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PatchAsync("/api/questions/999999", new JsonObject { ["marks"] = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/questions/999999")).StatusCode);
    }

    [Theory]
    [InlineData("chapterId missing", "chapterId")]
    [InlineData("chapter unknown", "chapterId")]
    [InlineData("text missing", "text")]
    [InlineData("text blank", "text")]
    [InlineData("marks missing", "marks")]
    [InlineData("marks not positive", "marks")]
    [InlineData("difficulty missing", "difficulty")]
    [InlineData("difficulty misspelt", "difficulty")]
    [InlineData("difficulty as a number", "difficulty")]
    [InlineData("type missing", "type")]
    [InlineData("type unknown", "type")]
    public async Task An_invalid_question_is_400_naming_the_field(string rule, string field)
    {
        var body = Question(await SeedChapterAsync(await SeedSubjectAsync()));
        switch (rule)
        {
            case "chapterId missing": body.Remove("chapterId"); break;
            case "chapter unknown": body["chapterId"] = 999_999; break;
            case "text missing": body.Remove("text"); break;
            case "text blank": body["text"] = " "; break;
            case "marks missing": body.Remove("marks"); break;
            case "marks not positive": body["marks"] = 0; break;
            case "difficulty missing": body.Remove("difficulty"); break;
            case "difficulty misspelt": body["difficulty"] = "Hardd"; break;
            case "difficulty as a number": body["difficulty"] = "3"; break;
            case "type missing": body.Remove("type"); break;
            case "type unknown": body["type"] = "Essay"; break;
        }

        await AssertFieldErrorAsync(await PostAsync("/api/questions", body), field);
    }

    [Fact]
    public async Task An_unused_question_can_be_edited_and_deleted()
    {
        var subjectId = await SeedSubjectAsync();
        var chapterA = await SeedChapterAsync(subjectId, orderIndex: 1);
        var chapterB = await SeedChapterAsync(subjectId, orderIndex: 2);
        var id = (int)(await CreateAsync(Question(chapterA)))["id"]!;

        var edited = await JsonAsync(
            await PatchAsync($"/api/questions/{id}", new JsonObject
            {
                ["chapterId"] = chapterB,
                ["text"] = "Define inertia.",
                ["marks"] = 2,
                ["difficulty"] = "Easy",
                ["type"] = "Short",
            }),
            HttpStatusCode.OK);

        Assert.Equal(chapterB, (int)edited["chapterId"]!);
        Assert.Equal("Define inertia.", (string)edited["text"]!);
        Assert.Equal(2, (int)edited["marks"]!);
        Assert.Equal("Easy", (string)edited["difficulty"]!);
        Assert.Equal("Short", (string)edited["type"]!);
        Assert.True(JsonNode.DeepEquals(edited, await JsonAsync(await Client.GetAsync($"/api/questions/{id}"), HttpStatusCode.OK)));

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/questions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/questions/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_patch_without_fields_changes_nothing()
    {
        var id = (int)(await CreateAsync(Question(await SeedChapterAsync(await SeedSubjectAsync()))))["id"]!;
        var before = await JsonAsync(await Client.GetAsync($"/api/questions/{id}"), HttpStatusCode.OK);

        var after = await JsonAsync(await PatchAsync($"/api/questions/{id}", new JsonObject()), HttpStatusCode.OK);

        Assert.True(JsonNode.DeepEquals(before, after));
    }

    [Theory]
    [InlineData("text blank", "text")]
    [InlineData("marks not positive", "marks")]
    [InlineData("difficulty misspelt", "difficulty")]
    [InlineData("type unknown", "type")]
    [InlineData("chapter unknown", "chapterId")]
    public async Task An_invalid_patch_is_400_naming_the_field(string rule, string field)
    {
        var id = (int)(await CreateAsync(Question(await SeedChapterAsync(await SeedSubjectAsync()))))["id"]!;
        var patch = rule switch
        {
            "text blank" => new JsonObject { ["text"] = "" },
            "marks not positive" => new JsonObject { ["marks"] = -1 },
            "difficulty misspelt" => new JsonObject { ["difficulty"] = "Hardd" },
            "type unknown" => new JsonObject { ["type"] = "Essay" },
            "chapter unknown" => new JsonObject { ["chapterId"] = 999_999 },
            _ => throw new ArgumentOutOfRangeException(nameof(rule)),
        };

        await AssertFieldErrorAsync(await PatchAsync($"/api/questions/{id}", patch), field);
    }
}
