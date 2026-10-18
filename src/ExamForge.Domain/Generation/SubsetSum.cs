namespace ExamForge.Domain.Generation;

public static class SubsetSum
{
    /// <summary>
    /// True if some subset of <paramref name="marks"/> sums to exactly <paramref name="target"/>
    /// (the empty subset sums to 0). Exact dynamic programming over sums 0..target, so it has
    /// no search cap: O(marks.Count * target) time, O(target) memory.
    /// </summary>
    public static bool CanReach(IReadOnlyList<int> marks, int target)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(target);

        var reachable = new bool[target + 1];
        reachable[0] = true;

        foreach (var mark in marks)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mark, nameof(marks));

            // Walk downwards so each question is used at most once.
            for (var sum = target; sum >= mark; sum--)
            {
                if (reachable[sum - mark])
                {
                    reachable[sum] = true;
                }
            }
        }

        return reachable[target];
    }
}
