using ExamForge.Domain.Catalog;
using ExamForge.Domain.Entities;
using ExamForge.Domain.Enums;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Infrastructure.Catalog;

public sealed class QuestionService(IDbContextFactory<ExamForgeDbContext> contextFactory, TimeProvider timeProvider)
    : IQuestionService
{
    public const int MaxEditAttempts = 3;

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
        for (var attempt = 1; ; attempt++)
        {
            // A fresh context per attempt: the retry re-loads the question and re-checks usage.
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                return await UpdateOnceAsync(context, id, changes, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxEditAttempts)
            {
                // A generation (or another edit) for the subject committed after this attempt read
                // the subject's version. It may have put this question in a paper: start again.
            }
            catch (DbUpdateConcurrencyException)
            {
                return new CatalogResult<QuestionInfo>.Conflict(
                    CatalogErrors.ConcurrentEdit,
                    $"Question {id} could not be saved: papers for its subject kept being generated at the same time. "
                    + "Try again.");
            }
        }
    }

    private async Task<CatalogResult<QuestionInfo>> UpdateOnceAsync(
        ExamForgeDbContext context,
        int id,
        QuestionChanges changes,
        CancellationToken cancellationToken)
    {
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

        // An edit to anything but isActive changes what a paper would show, so it must be
        // serialised against generation, which updates the Subject row on every paper. The
        // subjects (old chapter's and, on a move, new chapter's) are loaded tracked, with their
        // xmin, BEFORE the usage check: a paper committed after this read changes that xmin, so
        // either the check below sees the paper or the save below fails and the edit starts again.
        var subjects = changed.Count == 0
            ? []
            : await context.Subjects
                .Where(s => s.Chapters.Any(c => c.Id == question.ChapterId || c.Id == changes.ChapterId))
                .OrderBy(s => s.Id)
                .ToListAsync(cancellationToken);

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

        // Force an UPDATE of each subject row without changing any value: marking the property
        // modified makes EF write it back as-is, "WHERE xmin = <version read>". Postgres gives the
        // row a new version (new xmin) on every UPDATE, even an identical one, so a generation that
        // read the old version will fail its own save and retry. LastGeneratedAt keeps its value.
        foreach (var subject in subjects)
        {
            context.Entry(subject).Property(s => s.LastGeneratedAt).IsModified = true;
        }

        // One SaveChanges, one transaction: the question and the subject touches commit together.
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
