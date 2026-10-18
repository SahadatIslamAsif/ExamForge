namespace ExamForge.Domain.Generation;

/// <summary>How many marks one chapter must contribute. OrderIndex is the chapter's position in its subject.</summary>
public sealed record ChapterRequest(int ChapterId, string ChapterName, int OrderIndex, int RequestedMarks);
