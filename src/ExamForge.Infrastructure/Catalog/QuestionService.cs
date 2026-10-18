using ExamForge.Domain.Catalog;
using ExamForge.Domain.Entities;
using ExamForge.Domain.Enums;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Infrastructure.Catalog;

public sealed class QuestionService(IDbContextFactory<ExamForgeDbContext> contextFactory, TimeProvider timeProvider)
    : IQuestionService
{
    public async Task<CatalogResult<QuestionInfo>> CreateAsync(NewQuestion question, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (!await context.Chapters.AnyAsync(c => c.Id == question.ChapterId, cancellationToken))
        {
            return new CatalogResult<QuestionInfo>.Invalid("chapterId", $"Chapter {question.ChapterId} does not exist.");
        }

        var entity = new Question
        {
            ChapterId = question.ChapterId,
            Text = question.Text,
            Marks = question.Marks,
            Difficulty = question.Difficulty,
            Type = question.Type,
            IsActive = question.IsActive,
            CreatedAt = TruncateToMicroseconds(timeProvider.GetUtcNow()),
        };
        context.Questions.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return new CatalogResult<QuestionInfo>.Ok(ToInfo(entity, usedInPapers: 0));
    }

    public async Task<IReadOnlyList<QuestionInfo>> ListAsync(
        int? chapterId,
        Difficulty? difficulty,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Questions.AsNoTracking();
        if (chapterId is not null)
        {
            query = query.Where(q => q.ChapterId == chapterId);
        }

        if (difficulty is not null)
        {
            query = query.Where(q => q.Difficulty == difficulty);
        }

        return await Project(query.OrderBy(q => q.Id)).ToListAsync(cancellationToken);
    }

    public async Task<QuestionInfo?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Project(context.Questions.AsNoTracking().Where(q => q.Id == id)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CatalogResult<QuestionInfo>> UpdateAsync(
        int id,
        QuestionChanges changes,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var question = await context.Questions.SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (question is null)
        {
            return new CatalogResult<QuestionInfo>.NotFound();
        }

        // A field counts as changed only when it is sent with a different value.
        var changed = new[]
            {
                ("chapterId", changes.ChapterId is { } chapterId && chapterId != question.ChapterId),
                ("text", changes.Text is { } text && text != question.Text),
                ("marks", changes.Marks is { } marks && marks != question.Marks),
                ("difficulty", changes.Difficulty is { } difficulty && difficulty != question.Difficulty),
                ("type", changes.Type is { } type && type != question.Type),
            }
            .Where(field => field.Item2)
            .Select(field => field.Item1)
            .ToList();

        var usedInPapers = await UsageAsync(context, id, cancellationToken);
        if (usedInPapers > 0 && changed.Count > 0)
        {
            return new CatalogResult<QuestionInfo>.Conflict(
                CatalogErrors.QuestionInUse,
                $"Question {id} is used in {Papers(usedInPapers)}, so only isActive can change; "
                + $"this request changes {string.Join(", ", changed)}. "
                + "Editing it would rewrite those papers. Deactivate it and create a new question instead.");
        }

        if (changed.Contains("chapterId")
            && !await context.Chapters.AnyAsync(c => c.Id == changes.ChapterId, cancellationToken))
        {
            return new CatalogResult<QuestionInfo>.Invalid("chapterId", $"Chapter {changes.ChapterId} does not exist.");
        }

        question.ChapterId = changes.ChapterId ?? question.ChapterId;
        question.Text = changes.Text ?? question.Text;
        question.Marks = changes.Marks ?? question.Marks;
        question.Difficulty = changes.Difficulty ?? question.Difficulty;
        question.Type = changes.Type ?? question.Type;
        question.IsActive = changes.IsActive ?? question.IsActive;

        await context.SaveChangesAsync(cancellationToken);
        return new CatalogResult<QuestionInfo>.Ok(ToInfo(question, usedInPapers));
    }

    public async Task<CatalogResult<int>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (!await context.Questions.AnyAsync(q => q.Id == id, cancellationToken))
        {
            return new CatalogResult<int>.NotFound();
        }

        // The common case, with a message that names the papers. A paper committed between this
        // check and the DELETE is still refused by the foreign key; DatabaseConflicts maps that.
        var usedInPapers = await UsageAsync(context, id, cancellationToken);
        if (usedInPapers > 0)
        {
            return new CatalogResult<int>.Conflict(
                CatalogErrors.QuestionInUse,
                $"Question {id} is used in {Papers(usedInPapers)}, so it cannot be deleted. "
                + "Deactivate it instead (PATCH with \"isActive\": false).");
        }

        return await DatabaseConflicts.GuardAsync<int>(async () =>
        {
            await context.Questions.Where(q => q.Id == id).ExecuteDeleteAsync(cancellationToken);
            return new CatalogResult<int>.Ok(id);
        });
    }

    private static Task<int> UsageAsync(ExamForgeDbContext context, int questionId, CancellationToken cancellationToken) =>
        context.PaperQuestions.CountAsync(pq => pq.QuestionId == questionId, cancellationToken);

    private static IQueryable<QuestionInfo> Project(IQueryable<Question> questions) =>
        questions.Select(q => new QuestionInfo(
            q.Id, q.ChapterId, q.Text, q.Marks, q.Difficulty, q.Type, q.IsActive, q.CreatedAt, q.PaperQuestions.Count));

    private static QuestionInfo ToInfo(Question q, int usedInPapers) =>
        new(q.Id, q.ChapterId, q.Text, q.Marks, q.Difficulty, q.Type, q.IsActive, q.CreatedAt, usedInPapers);

    private static string Papers(int count) => count == 1 ? "1 paper" : $"{count} papers";

    // Postgres keeps microseconds; truncating first makes the 201 body equal a later GET.
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
}
