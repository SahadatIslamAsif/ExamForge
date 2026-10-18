using ExamForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Infrastructure.Persistence;

public class ExamForgeDbContext(DbContextOptions<ExamForgeDbContext> options) : DbContext(options)
{
    public DbSet<Subject> Subjects => Set<Subject>();

    public DbSet<Chapter> Chapters => Set<Chapter>();

    public DbSet<Question> Questions => Set<Question>();

    public DbSet<Paper> Papers => Set<Paper>();

    public DbSet<PaperQuestion> PaperQuestions => Set<PaperQuestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Picks up every IEntityTypeConfiguration<T> in this assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExamForgeDbContext).Assembly);
    }
}
