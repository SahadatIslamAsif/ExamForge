using System.Net;
using System.Text.Json.Nodes;
using ExamForge.Tests.Persistence;

namespace ExamForge.Tests.Api;

// Every rule in SPEC's validation list, plus the safe-integer seed rule. Each case breaks one
// rule of an otherwise valid request and expects a 400 naming that field.
[Collection(PostgresCollection.Name)]
public class GenerateValidationApiTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Theory]
    [InlineData("subjectId missing", "subjectId")]
    [InlineData("title missing", "title")]
    [InlineData("title blank", "title")]
    [InlineData("totalMarks missing", "totalMarks")]
    [InlineData("totalMarks not positive", "totalMarks")]
    [InlineData("distribution missing", "chapterDistribution")]
    [InlineData("distribution empty", "chapterDistribution")]
    [InlineData("distribution does not sum to totalMarks", "chapterDistribution")]
    [InlineData("entry marks not positive", "chapterDistribution[1].marks")]
    [InlineData("entry chapterId missing", "chapterDistribution[1].chapterId")]
    [InlineData("duplicate chapter", "chapterDistribution[1].chapterId")]
    [InlineData("mix missing", "difficultyMix")]
    [InlineData("share missing", "difficultyMix.medium")]
    [InlineData("share below 0", "difficultyMix.easy")]
    [InlineData("share above 1", "difficultyMix.hard")]
    [InlineData("shares do not sum to 1", "difficultyMix")]
    [InlineData("excludeLastNPapers missing", "excludeLastNPapers")]
    [InlineData("excludeLastNPapers negative", "excludeLastNPapers")]
    [InlineData("seed above the safe integer range", "seed")]
    [InlineData("seed below the safe integer range", "seed")]
    public async Task An_invalid_request_is_400_naming_the_field_and_saves_nothing(string rule, string field)
    {
        var bank = await SeedBankAsync([5, 5, 5], [5, 5, 5]);
        var request = Request(bank, [10, 5]);
        Break(request, rule);

        var body = await JsonAsync(await PostGenerateAsync(request), HttpStatusCode.BadRequest);

        var errors = body["errors"]!.AsObject();
        Assert.True(errors.ContainsKey(field), $"Expected an error for '{field}', got: {errors.ToJsonString()}");
        Assert.NotEmpty(errors[field]!.AsArray());
        Assert.Equal(0, await PaperCountAsync(bank.SubjectId));
    }

    [Fact]
    public async Task The_seed_error_explains_the_javascript_limit()
    {
        var bank = await SeedBankAsync([5]);
        var request = Request(bank, [5]);
        request["seed"] = 9_007_199_254_740_992;

        var body = await JsonAsync(await PostGenerateAsync(request), HttpStatusCode.BadRequest);

        Assert.Contains("JavaScript", (string)body["errors"]!["seed"]![0]!);
    }

    [Fact]
    public async Task A_chapter_from_another_subject_is_400_on_the_distribution()
    {
        var bank = await SeedBankAsync([5], [5]);
        var other = await SeedBankAsync([5], [5]);
        var request = Request(bank, [5, 5]);
        request["chapterDistribution"]![1]!["chapterId"] = other.ChapterIds[0];

        var body = await JsonAsync(await PostGenerateAsync(request), HttpStatusCode.BadRequest);

        var message = (string)body["errors"]!["chapterDistribution"]![0]!;
        Assert.Contains(other.ChapterIds[0].ToString(), message);
        Assert.Equal(0, await PaperCountAsync(bank.SubjectId));
        Assert.Equal(0, await PaperCountAsync(other.SubjectId));
    }

    private static void Break(JsonObject request, string rule)
    {
        var distribution = request["chapterDistribution"]!.AsArray();
        var mix = request["difficultyMix"]!.AsObject();

        switch (rule)
        {
            case "subjectId missing": request.Remove("subjectId"); break;
            case "title missing": request.Remove("title"); break;
            case "title blank": request["title"] = "   "; break;
            case "totalMarks missing": request.Remove("totalMarks"); break;
            case "totalMarks not positive": request["totalMarks"] = 0; break;
            case "distribution missing": request.Remove("chapterDistribution"); break;
            case "distribution empty": distribution.Clear(); break;
            case "distribution does not sum to totalMarks": request["totalMarks"] = 16; break;
            case "entry marks not positive":
                distribution[1]!["marks"] = 0;
                request["totalMarks"] = 10;
                break;
            case "entry chapterId missing": distribution[1]!.AsObject().Remove("chapterId"); break;
            case "duplicate chapter": distribution[1]!["chapterId"] = (int)distribution[0]!["chapterId"]!; break;
            case "mix missing": request.Remove("difficultyMix"); break;
            case "share missing": mix.Remove("medium"); break;
            case "share below 0":
                mix["easy"] = -0.1;
                mix["medium"] = 0.9;
                break;
            case "share above 1":
                mix["easy"] = -0.1;
                mix["medium"] = -0.1;
                mix["hard"] = 1.2;
                break;
            case "shares do not sum to 1": mix["hard"] = 0.3; break;
            case "excludeLastNPapers missing": request.Remove("excludeLastNPapers"); break;
            case "excludeLastNPapers negative": request["excludeLastNPapers"] = -1; break;
            case "seed above the safe integer range": request["seed"] = 9_007_199_254_740_992; break;
            case "seed below the safe integer range": request["seed"] = -9_007_199_254_740_992; break;
            default: throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown rule.");
        }
    }
}
