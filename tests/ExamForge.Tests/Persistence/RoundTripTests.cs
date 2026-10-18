using ExamForge.Domain.Entities;
using ExamForge.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public class RoundTripTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    [Fact]
    public async Task Enums_are_stored_as_their_names()
    {
        var chapterId = await SeedChapterAsync(await SeedSubjectAsync());
        int questionId;
        await using (var context = NewContext())
        {
            var question = NewQuestion(chapterId, marks: 3);
            question.Difficulty = Difficulty.Hard;
            question.Type = QuestionType.Structured;
            context.Questions.Add(question);
            await context.SaveChangesAsync();
            questionId = question.Id;
        }

        Assert.Equal("Hard", await ScalarAsync<string>($"""SELECT "Difficulty" FROM "Questions" WHERE "Id" = {questionId}"""));
        Assert.Equal("Structured", await ScalarAsync<string>($"""SELECT "Type" FROM "Questions" WHERE "Id" = {questionId}"""));
    }

    [Fact]
    public async Task A_utc_timestamp_round_trips()
    {
        var chapterId = await SeedChapterAsync(await SeedSubjectAsync());
        // Whole microseconds, because timestamptz stores microsecond precision.
        var createdAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero).AddTicks(1230);

        int questionId;
        await using (var context = NewContext())
        {
            var question = NewQuestion(chapterId, marks: 2);
            question.CreatedAt = createdAt;
            context.Questions.Add(question);
            await context.SaveChangesAsync();
            questionId = question.Id;
        }

        await using var check = NewContext();
        var loaded = await check.Questions.AsNoTracking().SingleAsync(q => q.Id == questionId);
        Assert.Equal(createdAt, loaded.CreatedAt);
        Assert.Equal(TimeSpan.Zero, loaded.CreatedAt.Offset);
    }

    // Documents a Npgsql rule rather than our own: timestamptz columns take only offset-0
    // values. Anything that stamps a time (CreatedAt, LastGeneratedAt) must use UTC.
    [Fact]
    public async Task A_non_utc_timestamp_is_rejected_by_npgsql_before_it_reaches_the_database()
    {
        var chapterId = await SeedChapterAsync(await SeedSubjectAsync());

        var thrown = await Record.ExceptionAsync(async () =>
        {
            await using var context = NewContext();
            var question = NewQuestion(chapterId, marks: 2);
            question.CreatedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(5.5));
            context.Questions.Add(question);
            await context.SaveChangesAsync();
        });

        Assert.NotNull(thrown);
        Assert.Contains("offset", FullMessage(thrown), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Chain(thrown), ex => ex is Npgsql.PostgresException);
    }

    // Whitespace, key order and a duplicate key: all three are lost by jsonb, kept by json.
    [Fact]
    public async Task SpecJson_comes_back_exactly_as_it_was_stored()
    {
        const string spec = "{ \"title\":  \"Mock\",\n  \"b\": 1,   \"a\": [1,2],\n  \"b\": 2 }";
        var subjectId = await SeedSubjectAsync();
        var paperId = await SeedPaperAsync(subjectId, spec);

        await using var context = NewContext();
        var loaded = await context.Papers.AsNoTracking().SingleAsync(p => p.Id == paperId);
        Assert.Equal(spec, loaded.SpecJson);

        var storedText = await ScalarAsync<string>($"""SELECT "SpecJson"::text FROM "Papers" WHERE "Id" = {paperId}""");
        Assert.Equal(spec, storedText);
    }

    private static IEnumerable<Exception> Chain(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static string FullMessage(Exception ex) => string.Join(" | ", Chain(ex).Select(e => e.Message));
}
