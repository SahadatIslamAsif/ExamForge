using ExamForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExamForge.Infrastructure.Persistence.Configurations;

public class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Level).HasMaxLength(100).IsRequired();

        // A uint row version maps to Postgres's xmin system column: no real column is
        // created, and every UPDATE of the row changes it. EF adds "WHERE xmin = @old"
        // to the UPDATE and throws DbUpdateConcurrencyException if no row matched.
        builder.Property(s => s.Version).IsRowVersion();
    }
}
