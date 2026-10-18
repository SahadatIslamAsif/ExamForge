using ExamForge.Domain.Enums;

namespace ExamForge.Domain.Generation;

/// <summary>A question as the solver sees it: no text, no EF entity, just what selection needs.</summary>
public sealed record SolverQuestion(int Id, int ChapterId, int Marks, Difficulty Difficulty);
