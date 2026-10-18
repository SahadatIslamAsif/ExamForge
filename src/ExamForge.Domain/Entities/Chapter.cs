namespace ExamForge.Domain.Entities;

public class Chapter
{
    public int Id { get; set; }

    public int SubjectId { get; set; }

    public Subject Subject { get; set; } = null!;

    public required string Name { get; set; }

    // Position within the subject; unique per subject.
    public int OrderIndex { get; set; }

    public List<Question> Questions { get; set; } = [];
}
