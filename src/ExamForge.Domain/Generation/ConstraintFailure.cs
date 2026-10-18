namespace ExamForge.Domain.Generation;

public enum FailureConstraint
{
    /// <summary>The eligible pool has fewer marks than requested.</summary>
    ChapterDistribution,

    /// <summary>The pool has enough marks in total, but no subset sums to exactly the target.</summary>
    ExactMarks,
}

/// <summary>
/// One constraint the bank cannot meet. The API layer turns this into the 422 body and
/// writes the human-readable "detail", which can use the figures here; the exclusion
/// window ("after excluding the last 3 papers") is request context only the API knows.
/// </summary>
public sealed record ConstraintFailure(
    FailureConstraint Constraint,
    int ChapterId,
    string ChapterName,
    int RequestedMarks,
    int AvailableMarks)
{
    /// <summary>Marks missing from the pool. Zero or negative when the pool is large enough (e.g. ExactMarks).</summary>
    public int ShortfallMarks => RequestedMarks - AvailableMarks;
}
