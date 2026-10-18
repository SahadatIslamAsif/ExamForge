using Microsoft.EntityFrameworkCore;

namespace ExamForge.Tests.Persistence;

// SqlState 23514 is Postgres's check_violation.
[Collection(PostgresCollection.Name)]
public class QuestionConstraintTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Marks_that_are_not_positive_are_rejected(int marks)
    {
        var chapterId = await SeedChapterAsync(await SeedSubjectAsync());

        await AssertPostgresErrorAsync("23514", async () =>
        {
            await using var context = NewContext();
            context.Questions.Add(NewQuestion(chapterId, marks));
            await context.SaveChangesAsync();
        });
    }

    [Theory]
    [InlineData("Harder", "Short")]
    [InlineData("Easy", "Essay")]
    public async Task Difficulty_or_type_that_is_not_an_enum_name_is_rejected(string difficulty, string type)
    {
        var chapterId = await SeedChapterAsync(await SeedSubjectAsync());

        // Raw SQL, because EF's string conversion would never produce these values itself.
        // The interpolated values become query parameters, not concatenated text.
        await AssertPostgresErrorAsync("23514", async () =>
        {
            await using var context = NewContext();
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "Questions" ("ChapterId", "Text", "Marks", "Difficulty", "Type", "IsActive", "CreatedAt")
                VALUES ({chapterId}, 'raw', 2, {difficulty}, {type}, true, now())
                """);
        });
    }
}
