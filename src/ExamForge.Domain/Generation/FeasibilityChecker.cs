namespace ExamForge.Domain.Generation;

public static class FeasibilityChecker
{
    /// <summary>
    /// Checks every chapter and returns ALL failures, ordered by chapter OrderIndex, so the
    /// caller can report every shortfall at once. An empty result means every chapter's
    /// target is reachable. A chapter missing from <paramref name="pools"/> has an empty pool.
    /// </summary>
    public static IReadOnlyList<ConstraintFailure> Check(
        IReadOnlyList<ChapterRequest> requests,
        IReadOnlyDictionary<int, IReadOnlyList<SolverQuestion>> pools)
    {
        var failures = new List<ConstraintFailure>();

        foreach (var request in requests.OrderBy(r => r.OrderIndex))
        {
            var marks = pools.TryGetValue(request.ChapterId, out var pool)
                ? pool.Select(q => q.Marks).ToList()
                : [];
            var available = marks.Sum();

            if (available < request.RequestedMarks)
            {
                failures.Add(Failure(FailureConstraint.ChapterDistribution, request, available));
            }
            else if (!SubsetSum.CanReach(marks, request.RequestedMarks))
            {
                failures.Add(Failure(FailureConstraint.ExactMarks, request, available));
            }
        }

        return failures;
    }

    private static ConstraintFailure Failure(FailureConstraint constraint, ChapterRequest request, int available) =>
        new(constraint, request.ChapterId, request.ChapterName, request.RequestedMarks, available);
}
