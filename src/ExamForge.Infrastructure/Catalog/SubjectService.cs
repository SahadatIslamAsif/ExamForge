using ExamForge.Domain.Catalog;
using ExamForge.Domain.Entities;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Infrastructure.Catalog;

public sealed class SubjectService(IDbContextFactory<ExamForgeDbContext> contextFactory) : ISubjectService
{
    public async Task<SubjectInfo> CreateAsync(NewSubject subject, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new Subject { Name = subject.Name, Level = subject.Level };
        context.Subjects.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return ToInfo(entity);
    }

    public async Task<IReadOnlyList<SubjectInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Subjects.AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new SubjectInfo(s.Id, s.Name, s.Level))
            .ToListAsync(cancellationToken);
    }

    public async Task<SubjectInfo?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Subjects.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SubjectInfo(s.Id, s.Name, s.Level))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static SubjectInfo ToInfo(Subject subject) => new(subject.Id, subject.Name, subject.Level);
}
