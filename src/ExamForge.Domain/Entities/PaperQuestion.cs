namespace ExamForge.Domain.Entities;

// Join between Paper and Question that also carries the question's position.
public class PaperQuestion
{
    public int Id { get; set; }

    public int PaperId { get; set; }

    public Paper Paper { get; set; } = null!;

    public int QuestionId { get; set; }

    public Question Question { get; set; } = null!;

    public int OrderIndex { get; set; }
}
