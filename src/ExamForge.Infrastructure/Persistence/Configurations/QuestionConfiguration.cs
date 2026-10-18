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

        // The string conversion alone would accept any text, so the database enforces the
        // allowed names itself. Keep these lists in step with the enums.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Questions_Marks_Positive", "\"Marks\" > 0");
            t.HasCheckConstraint("CK_Questions_Difficulty_Valid", "\"Difficulty\" IN ('Easy', 'Medium', 'Hard')");
            t.HasCheckConstraint("CK_Questions_Type_Valid", "\"Type\" IN ('Mcq', 'Short', 'Structured')");
        });

        builder.HasOne(q => q.Chapter)
            .WithMany(c => c.Questions)
            .HasForeignKey(q => q.ChapterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
