using System.Text.Json.Nodes;
using ExamForge.Domain.Entities;
using ExamForge.Infrastructure.Seeding;
using ExamForge.Tests.Api;
using ExamForge.Tests.GenerationService;
using ExamForge.Tests.Persistence;
using ExamForge.Tests.Seeding;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamForge.Tests.EndToEnd;

public sealed record BankQuestion(int Id, int ChapterId, int Marks, bool IsActive);

public sealed record BankChapter(int Id, string Name, int OrderIndex, IReadOnlyList<BankQuestion> Questions);

public sealed record BankSubject(int Id, string Name, IReadOnlyList<BankChapter> Chapters);

/// <summary>A generate request as sent, plus what the test needs to judge the response.</summary>
public sealed record RandomSpec(
    BankSubject Subject,
    IReadOnlyList<(int ChapterId, int Marks)> Distribution,
    int ExcludeLastNPapers,
    long? Seed,
    JsonObject Body)
{
    public int TotalMarks => Distribution.Sum(d => d.Marks);
}

/// <summary>
/// Tests that drive the real API over HTTP against the real seed bank. Everything the tests use to
/// judge a response (the bank, the history, feasibility) is read or computed here, independently
/// of the service code under test.
/// </summary>
public abstract class EndToEndTestBase(PostgresFixture postgres) : ApiTestBase(postgres)
{
    /// <summary>Seeds this class's database with seed/questions.json through the real seeder.</summary>
    protected async Task<BankSubject> SeedRealBankAsync()
    {
        await new QuestionBankSeeder(new TestDbContextFactory(Database, null), Clock)
            .SeedAsync(QuestionBankFile.Load(SeedFileTests.BankPath()));
        return (await LoadBankAsync()).Single();
    }

    /// <summary>
    /// Inserts a second subject built from the same bank file. With two subjects, a history query
    /// that forgot to filter by subject would exclude the wrong questions, and the tests would see it.
    /// </summary>
    protected async Task AddSecondCopyOfBankAsync()
    {
        var source = QuestionBankFile.Load(SeedFileTests.BankPath()).Subjects.Single();
        await using var context = NewContext();
        context.Subjects.Add(new Subject
        {
            Name = $"{source.Name} (second copy)",
            Level = source.Level,
            Chapters = source.Chapters.Select(c => new Chapter
            {
                Name = c.Name,
                OrderIndex = c.OrderIndex,
                Questions = c.Questions.Select(q => new Question
                {
                    Text = q.Text,
                    Marks = q.Marks,
                    Difficulty = q.Difficulty,
                    Type = q.Type,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                }).ToList(),
            }).ToList(),
        });
        await context.SaveChangesAsync();
    }

    protected async Task<IReadOnlyList<BankSubject>> LoadBankAsync()
    {
        await using var context = NewContext();
        var questions = await context.Questions.AsNoTracking()
            .OrderBy(q => q.Id)
            .Select(q => new BankQuestion(q.Id, q.ChapterId, q.Marks, q.IsActive))
            .ToListAsync();
        var chapters = await context.Chapters.AsNoTracking().OrderBy(c => c.OrderIndex).ToListAsync();
        var subjects = await context.Subjects.AsNoTracking().OrderBy(s => s.Id).ToListAsync();

        return subjects.Select(s => new BankSubject(
                s.Id,
                s.Name,
                chapters.Where(c => c.SubjectId == s.Id)
                    .Select(c => new BankChapter(c.Id, c.Name, c.OrderIndex, questions.Where(q => q.ChapterId == c.Id).ToList()))
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// Question ids in the subject's last N papers, by CreatedAt DESC, Id DESC: written as plain SQL
    /// so it does not share any code with the service's own history query.
    /// </summary>
    protected async Task<HashSet<int>> HistoryAsync(int subjectId, int lastN)
    {
        if (lastN == 0)
        {
            return [];
        }

        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT pq."QuestionId" FROM "PaperQuestions" pq
            WHERE pq."PaperId" IN (
                SELECT p."Id" FROM "Papers" p WHERE p."SubjectId" = @subject
                ORDER BY p."CreatedAt" DESC, p."Id" DESC LIMIT @n)
            """,
            connection);
        command.Parameters.AddWithValue("subject", subjectId);
        command.Parameters.AddWithValue("n", lastN);

        var ids = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    /// <summary>
    /// The independent oracle: does any subset of these marks sum to exactly the target? Plain
    /// enumeration of all 2^n subsets, nothing shared with the solver's dynamic programming.
    /// </summary>
    protected static bool SomeSubsetSumsTo(IReadOnlyList<int> marks, int target)
    {
        if (marks.Count > 20)
        {
            throw new InvalidOperationException($"Brute force over {marks.Count} questions is too slow; the oracle expects a chapter-sized pool.");
        }

        for (var mask = 0; mask < 1 << marks.Count; mask++)
        {
            var sum = 0;
            for (var i = 0; i < marks.Count; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    sum += marks[i];
                }
            }

            if (sum == target)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A valid random request for the subject. Every draw comes from the given Random.</summary>
    protected static RandomSpec NextSpec(Random random, BankSubject subject, int? excludeLastNPapers = null)
    {
        var chapterCount = random.Next(1, Math.Min(6, subject.Chapters.Count) + 1);
        var distribution = subject.Chapters
            .OrderBy(_ => random.Next())
            .Take(chapterCount)
            .Select(c => (c.Id, Marks: random.Next(1, 21)))
            .ToList();

        // Whole-number weights, so the shares are exact enough to sum to 1 within the API's tolerance.
        int easy, medium, hard;
        do
        {
            (easy, medium, hard) = (random.Next(0, 11), random.Next(0, 11), random.Next(0, 11));
        }
        while (easy + medium + hard == 0);
        double total = easy + medium + hard;

        var exclude = excludeLastNPapers ?? random.Next(0, 5);
        long? seed = random.Next(2) == 0 ? random.NextInt64(0, 1_000_000_000) : null;

        var body = new JsonObject
        {
            ["subjectId"] = subject.Id,
            ["title"] = "Property test paper",
            ["totalMarks"] = distribution.Sum(d => d.Marks),
            ["chapterDistribution"] = new JsonArray(distribution
                .Select(d => (JsonNode)new JsonObject { ["chapterId"] = d.Id, ["marks"] = d.Marks })
                .ToArray()),
            ["difficultyMix"] = new JsonObject { ["easy"] = easy / total, ["medium"] = medium / total, ["hard"] = hard / total },
            ["excludeLastNPapers"] = exclude,
        };
        if (seed is not null)
        {
            body["seed"] = seed;
        }

        return new RandomSpec(subject, distribution.Select(d => (d.Id, d.Marks)).ToList(), exclude, seed, body);
    }

    protected static async Task<(int Status, JsonNode? Body, string Text)> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        JsonNode? body = null;
        try
        {
            body = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            // Left null; the caller reports the raw text.
        }

        return ((int)response.StatusCode, body, text);
    }
}
