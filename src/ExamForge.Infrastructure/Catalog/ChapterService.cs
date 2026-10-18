using ExamForge.Domain.Catalog;
using ExamForge.Domain.Entities;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Infrastructure.Catalog;

public sealed class ChapterService(IDbContextFactory<ExamForgeDbContext> contextFactory) : IChapterService
{
    public async Task<CatalogResult<ChapterInfo>> CreateAsync(NewChapter chapter, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (!await context.Subjects.AnyAsync(s => s.Id == chapter.SubjectId, cancellationToken))
        {
            return new CatalogResult<ChapterInfo>.Invalid("subjectId", $"Subject {chapter.SubjectId} does not exist.");
        }

        // No pre-check for a taken orderIndex: the unique index is the single source of truth,
        // and DatabaseConflicts turns its violation into CHAPTER_ORDER_TAKEN.
        return await DatabaseConflicts.GuardAsync<ChapterInfo>(async () =>
        {
            var entity = new Chapter { SubjectId = chapter.SubjectId, Name = chapter.Name, OrderIndex = chapter.OrderIndex };
            context.Chapters.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
            return new CatalogResult<ChapterInfo>.Ok(new ChapterInfo(entity.Id, entity.SubjectId, entity.Name, entity.OrderIndex));
        });
    }

    public async Task<IReadOnlyList<ChapterInfo>?> ListForSubjectAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (!await context.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken))
        {
            return null;
        }

        return await context.Chapters.AsNoTracking()
            .Where(c => c.SubjectId == subjectId)
            .OrderBy(c => c.OrderIndex)
            .Select(c => new ChapterInfo(c.Id, c.SubjectId, c.Name, c.OrderIndex))
            .ToListAsync(cancellationToken);
    }

    public async Task<ChapterInfo?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Chapters.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ChapterInfo(c.Id, c.SubjectId, c.Name, c.OrderIndex))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
