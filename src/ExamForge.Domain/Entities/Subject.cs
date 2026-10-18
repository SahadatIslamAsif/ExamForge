namespace ExamForge.Domain.Entities;

public class Subject
{
    public int Id { get; set; }

    public required string Name { get; set; }

    // e.g. "O Level", "Class 9"
    public required string Level { get; set; }

    // Set on every generation so the Subject row is always updated, which is
    // what makes the concurrency token fire (see "Concurrency" in the spec).
    public DateTimeOffset? LastGeneratedAt { get; set; }

    // Concurrency token. Infrastructure maps it to Postgres's xmin column.
    public uint Version { get; set; }

    public List<Chapter> Chapters { get; set; } = [];

    public List<Paper> Papers { get; set; } = [];
}
