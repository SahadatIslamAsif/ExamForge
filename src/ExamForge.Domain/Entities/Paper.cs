namespace ExamForge.Domain.Entities;

public class Paper
{
    public int Id { get; set; }

    public int SubjectId { get; set; }

    public Subject Subject { get; set; } = null!;

    public required string Title { get; set; }

    public int TotalMarks { get; set; }

    // Always set: the one supplied in the request, or the one picked at the API boundary.
    public long Seed { get; set; }

    // The originating request, stored verbatim so the paper can be audited or re-run.
    public required string SpecJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<PaperQuestion> PaperQuestions { get; set; } = [];
}
