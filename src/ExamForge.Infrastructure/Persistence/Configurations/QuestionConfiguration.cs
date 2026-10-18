using ExamForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExamForge.Infrastructure.Persistence.Configurations;

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.HasKey(q => q.Id);

        builder.Property(q => q.Text).IsRequired();

        // Enums are stored as their names so the table is readable in psql.
        builder.Property(q => q.Difficulty).HasConversion<string>().HasMaxLength(20);
        builder.Property(q => q.Type).HasConversion<string>().HasMaxLength(20);

        builder.ToTable(t => t.HasCheckConstraint("CK_Questions_Marks_Positive", "\"Marks\" > 0"));

        builder.HasOne(q => q.Chapter)
            .WithMany(c => c.Questions)
            .HasForeignKey(q => q.ChapterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
