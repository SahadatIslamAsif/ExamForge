using ExamForge.Domain.Entities;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Infrastructure.Seeding;

public sealed record SeedResult(bool Seeded, int MigrationsApplied, int Subjects, int Chapters, int Questions)
{
    public string Summary => Seeded
        ? $"Applied {Count(MigrationsApplied, "migration")}. "
          + $"Seeded {Count(Subjects, "subject")}, {Count(Chapters, "chapter")} and {Count(Questions, "question")}."
        : $"Applied {Count(MigrationsApplied, "migration")}. "
          + $"Skipped: the database already has {Count(Subjects, "subject")}, so nothing was changed.";

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}

/// <summary>
/// Loads the question bank into an empty database. Seeding only ever happens once: if any
/// subject exists, nothing is changed. All inserts share one transaction, so a failure part way
/// through leaves the database exactly as it was.
/// </summary>
public sealed class QuestionBankSeeder(IDbContextFactory<ExamForgeDbContext> contextFactory, TimeProvider timeProvider)
{
    public async Task<SeedResult> SeedAsync(QuestionBankFile bank, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
        await context.Database.MigrateAsync(cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existingSubjects = await context.Subjects.CountAsync(cancellationToken);
        if (existingSubjects > 0)
        {
            return new SeedResult(false, pending, existingSubjects, 0, 0);
        }

        var createdAt = TruncateToMicroseconds(timeProvider.GetUtcNow());
        int chapters = 0, questions = 0;

        foreach (var seedSubject in bank.Subjects)
        {
            var subject = new Subject { Name = seedSubject.Name, Level = seedSubject.Level };
            context.Subjects.Add(subject);
            await context.SaveChangesAsync(cancellationToken);

            // One save per chapter keeps each batch small; the transaction makes them all-or-nothing.
            foreach (var seedChapter in seedSubject.Chapters.OrderBy(c => c.OrderIndex))
            {
                context.Chapters.Add(new Chapter
                {
                    SubjectId = subject.Id,
                    Name = seedChapter.Name,
                    OrderIndex = seedChapter.OrderIndex,
                    Questions = seedChapter.Questions.Select(q => new Question
                    {
                        Text = q.Text,
                        Marks = q.Marks,
                        Difficulty = q.Difficulty,
                        Type = q.Type,
                        IsActive = true,
                        CreatedAt = createdAt,
                    }).ToList(),
                });
                await context.SaveChangesAsync(cancellationToken);

                chapters++;
                questions += seedChapter.Questions.Count;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new SeedResult(true, pending, bank.Subjects.Count, chapters, questions);
    }

    // Postgres keeps microseconds; truncating first keeps CreatedAt identical when read back.
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
}
