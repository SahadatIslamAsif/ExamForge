using ExamForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExamForge.Infrastructure.Persistence.Configurations;

public class PaperQuestionConfiguration : IEntityTypeConfiguration<PaperQuestion>
{
    public void Configure(EntityTypeBuilder<PaperQuestion> builder)
    {
        builder.HasKey(pq => pq.Id);

        // A paper's questions go with the paper.
        builder.HasOne(pq => pq.Paper)
            .WithMany(p => p.PaperQuestions)
            .HasForeignKey(pq => pq.PaperId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict is what makes "a question used in a paper cannot be deleted" hold
        // at the database level, not only in the service.
        builder.HasOne(pq => pq.Question)
            .WithMany(q => q.PaperQuestions)
            .HasForeignKey(pq => pq.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(pq => new { pq.PaperId, pq.QuestionId }).IsUnique();
        builder.HasIndex(pq => new { pq.PaperId, pq.OrderIndex }).IsUnique();
    }
}
