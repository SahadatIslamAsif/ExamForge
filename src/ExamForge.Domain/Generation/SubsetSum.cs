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

    /// <summary>
    /// Reachability of every suffix: <c>table[i][s]</c> is true when some subset of
    /// <c>marks[i..]</c> sums to exactly s, for s in 0..target. <c>table[marks.Count]</c> is true
    /// only at 0. The selection walk uses it to prune branches that cannot reach the target.
    /// </summary>
    public static bool[][] SuffixTable(IReadOnlyList<int> marks, int target)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(target);

        var table = new bool[marks.Count + 1][];
        table[marks.Count] = new bool[target + 1];
        table[marks.Count][0] = true;

        for (var i = marks.Count - 1; i >= 0; i--)
        {
            var mark = marks[i];
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mark, nameof(marks));

            table[i] = new bool[target + 1];
            for (var sum = 0; sum <= target; sum++)
            {
                table[i][sum] = table[i + 1][sum] || (sum >= mark && table[i + 1][sum - mark]);
            }
        }

        return table;
    }
}