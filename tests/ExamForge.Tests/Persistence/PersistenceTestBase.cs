using ExamForge.Domain.Entities;
using ExamForge.Domain.Enums;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamForge.Tests.Persistence;

// Base for the persistence tests. A new instance is created per test method, so
// InitializeAsync runs per test; the database itself is created once per test class.
// Tests in a class share that database, so each test seeds rows of its own.
public abstract class PersistenceTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await postgres.GetDatabaseAsync(GetType());

    public Task DisposeAsync() => Task.CompletedTask;

    protected ExamForgeDbContext NewContext() => _database.NewContext();

    protected async Task<int> SeedSubjectAsync()
    {
        await using var context = NewContext();
        var subject = new Subject { Name = $"subject-{Guid.NewGuid():N}", Level = "O Level" };
        context.Subjects.Add(subject);
        await context.SaveChangesAsync();
        return subject.Id;
    }

    protected async Task<int> SeedChapterAsync(int subjectId, int orderIndex = 1)
    {
        await using var context = NewContext();
        var chapter = new Chapter { SubjectId = subjectId, Name = "Chapter", OrderIndex = orderIndex };
        context.Chapters.Add(chapter);
        await context.SaveChangesAsync();
        return chapter.Id;
    }

    protected async Task<int> SeedQuestionAsync(int chapterId, int marks = 2)
    {
        await using var context = NewContext();
        var question = NewQuestion(chapterId, marks);
        context.Questions.Add(question);
        await context.SaveChangesAsync();
        return question.Id;
    }

    protected async Task<int> SeedPaperAsync(int subjectId, string specJson = "{}")
    {
        await using var context = NewContext();
        var paper = new Paper
        {
            SubjectId = subjectId,
            Title = "Paper",
            TotalMarks = 2,
            Seed = 42,
            SpecJson = specJson,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.Papers.Add(paper);
        await context.SaveChangesAsync();
        return paper.Id;
    }

    protected async Task AddToPaperAsync(int paperId, int questionId, int orderIndex)
    {
        await using var context = NewContext();
        context.PaperQuestions.Add(new PaperQuestion { PaperId = paperId, QuestionId = questionId, OrderIndex = orderIndex });
        await context.SaveChangesAsync();
    }

    protected static Question NewQuestion(int chapterId, int marks) => new()
    {
        ChapterId = chapterId,
        Text = "A question",
        Marks = marks,
        Difficulty = Difficulty.Medium,
        Type = QuestionType.Short,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // Reads one value with plain Npgsql, bypassing EF, to see what is really stored.
    protected async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    // Runs the action, expects the database to reject it, and returns the Postgres error.
    // SaveChanges wraps the Postgres error in a DbUpdateException; raw SQL and
    // ExecuteDeleteAsync throw it directly. This walks the inner exceptions to find it.
    protected static async Task<PostgresException> AssertPostgresErrorAsync(string sqlState, Func<Task> action)
    {
        var thrown = await Record.ExceptionAsync(action);
        Assert.NotNull(thrown);

        for (var ex = thrown; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException postgresError)
            {
                Assert.Equal(sqlState, postgresError.SqlState);
                return postgresError;
            }
        }

        throw new Xunit.Sdk.XunitException($"Expected a PostgresException ({sqlState}) but got: {thrown}");
    }
}
