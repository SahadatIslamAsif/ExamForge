namespace ExamForge.Domain.Entities;

using ExamForge.Domain.Enums;

public class Question
{
    public int Id { get; set; }

    public int ChapterId { get; set; }

    public Chapter Chapter { get; set; } = null!;

    public required string Text { get; set; }

    // Always positive; one fixed value per question (assumption A1).
    public int Marks { get; set; }

    public Difficulty Difficulty { get; set; }

    public QuestionType Type { get; set; }

    // Soft delete: keeps stored papers truthful.
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public List<PaperQuestion> PaperQuestions { get; set; } = [];
}
