using ExamForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExamForge.Infrastructure.Persistence.Configurations;

public class PaperConfiguration : IEntityTypeConfiguration<Paper>
{
    public void Configure(EntityTypeBuilder<Paper> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();

        // jsonb rather than json: smaller, and queryable if the history ever needs it.
        builder.Property(p => p.SpecJson).HasColumnType("jsonb").IsRequired();

        builder.HasOne(p => p.Subject)
            .WithMany(s => s.Papers)
            .HasForeignKey(p => p.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves the history query: the N most recent papers for a subject.
        builder.HasIndex(p => new { p.SubjectId, p.CreatedAt, p.Id })
            .IsDescending(false, true, true);
    }
}
