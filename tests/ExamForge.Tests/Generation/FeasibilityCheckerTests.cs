using ExamForge.Domain.Enums;
using ExamForge.Domain.Generation;

namespace ExamForge.Tests.Generation;

public class FeasibilityCheckerTests
{
    private static ChapterRequest Request(int chapterId, string name, int orderIndex, int marks) =>
        new(chapterId, name, orderIndex, marks);

    private static SolverQuestion Q(int id, int chapterId, int marks) =>
        new(id, chapterId, marks, Difficulty.Medium);

    private static IReadOnlyDictionary<int, IReadOnlyList<SolverQuestion>> Pools(
        params (int ChapterId, int[] Marks)[] chapters)
    {
        var nextId = 1;
        var pools = new Dictionary<int, IReadOnlyList<SolverQuestion>>();
        foreach (var (chapterId, marks) in chapters)
        {
            pools[chapterId] = marks.Select(m => Q(nextId++, chapterId, m)).ToList();
        }

        return pools;
    }

    [Fact]
    public void ChapterShortOnMarks_ReportsChapterDistributionWithRequestedAndAvailable()
    {
        var requests = new[] { Request(3, "Forces and Motion", 1, 20) };
        var pools = Pools((3, [5, 4, 3]));

        var failures = FeasibilityChecker.Check(requests, pools);

        var failure = Assert.Single(failures);
        Assert.Equal(FailureConstraint.ChapterDistribution, failure.Constraint);
        Assert.Equal(3, failure.ChapterId);
        Assert.Equal("Forces and Motion", failure.ChapterName);
        Assert.Equal(20, failure.RequestedMarks);
        Assert.Equal(12, failure.AvailableMarks);
    }

    [Fact]
    public void TwoShortChapters_BothAreReported()
    {
        var requests = new[]
        {
            Request(3, "Forces and Motion", 1, 20),
            Request(4, "Energy", 2, 12),
            Request(7, "Waves", 3, 6),
        };
        // Chapter 7 has enough (9 >= 6) and a reachable target, so only 3 and 4 fail.
        var pools = Pools((3, [5, 4, 3]), (4, [2, 2, 2, 2, 2]), (7, [3, 3, 3]));

        var failures = FeasibilityChecker.Check(requests, pools);

        Assert.Equal(2, failures.Count);
        Assert.All(failures, f => Assert.Equal(FailureConstraint.ChapterDistribution, f.Constraint));
        Assert.Equal([3, 4], failures.Select(f => f.ChapterId));
        Assert.Equal([12, 10], failures.Select(f => f.AvailableMarks));
    }

    [Fact]
    public void MixedFailures_AreAllReported_InChapterOrder()
    {
        var requests = new[]
        {
            Request(9, "Late chapter", 2, 9),
            Request(4, "Early chapter", 1, 50),
        };
        var pools = Pools((9, [2, 4, 6]), (4, [5, 5]));

        var failures = FeasibilityChecker.Check(requests, pools);

        Assert.Equal(2, failures.Count);
        Assert.Equal(4, failures[0].ChapterId);
        Assert.Equal(FailureConstraint.ChapterDistribution, failures[0].Constraint);
        Assert.Equal(9, failures[1].ChapterId);
        Assert.Equal(FailureConstraint.ExactMarks, failures[1].Constraint);
    }

    [Fact]
    public void EmptyPool_ReportsChapterDistributionWithZeroAvailable()
    {
        var requests = new[] { Request(3, "Forces and Motion", 1, 10) };
        var pools = Pools((3, []));

        var failure = Assert.Single(FeasibilityChecker.Check(requests, pools));

        Assert.Equal(FailureConstraint.ChapterDistribution, failure.Constraint);
        Assert.Equal(0, failure.AvailableMarks);
    }

    [Fact]
    public void ChapterMissingFromPools_IsTreatedAsEmpty()
    {
        var requests = new[] { Request(3, "Forces and Motion", 1, 10) };
        var pools = Pools();

        var failure = Assert.Single(FeasibilityChecker.Check(requests, pools));

        Assert.Equal(FailureConstraint.ChapterDistribution, failure.Constraint);
        Assert.Equal(0, failure.AvailableMarks);
    }

    [Fact]
    public void EnoughMarksButNoExactCombination_ReportsExactMarks()
    {
        var requests = new[] { Request(4, "Energy", 1, 9) };
        var pools = Pools((4, [2, 4, 6]));

        var failure = Assert.Single(FeasibilityChecker.Check(requests, pools));

        Assert.Equal(FailureConstraint.ExactMarks, failure.Constraint);
        Assert.Equal(4, failure.ChapterId);
        Assert.Equal("Energy", failure.ChapterName);
        Assert.Equal(9, failure.RequestedMarks);
        Assert.Equal(12, failure.AvailableMarks);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    public void ReachableTarget_HasNoFailure(int target)
    {
        var requests = new[] { Request(4, "Energy", 1, target) };
        var pools = Pools((4, [2, 4, 6]));

        Assert.Empty(FeasibilityChecker.Check(requests, pools));
    }

    [Fact]
    public void TargetEqualToWholePool_IsReachable()
    {
        var requests = new[] { Request(4, "Energy", 1, 7) };
        var pools = Pools((4, [1, 2, 4]));

        Assert.Empty(FeasibilityChecker.Check(requests, pools));
    }

    [Fact]
    public void NoRequests_HasNoFailures()
    {
        Assert.Empty(FeasibilityChecker.Check([], Pools()));
    }

    // Oracle test: the DP must agree with brute-force enumeration of every subset.
    // Pools use a shared "step" for some cases so that parity-style gaps (like all-even
    // marks and an odd target) actually occur; the counts at the end prove all three
    // outcomes were exercised, otherwise the test could agree trivially.
    [Fact]
    public void Checker_AgreesWithBruteForceOnRandomPools()
    {
        var random = new SplitMix64Random(20261018);
        var outcomes = new Dictionary<string, int> { ["ok"] = 0, ["short"] = 0, ["exact"] = 0 };

        for (var caseNumber = 0; caseNumber < 600; caseNumber++)
        {
            var step = 1 + random.NextInt(3);
            var size = random.NextInt(11);
            var marks = Enumerable.Range(0, size).Select(_ => step * (1 + random.NextInt(5))).ToArray();
            var target = 1 + random.NextInt(40);

            var requests = new[] { Request(1, "Chapter", 1, target) };
            var pools = Pools((1, marks));

            var failures = FeasibilityChecker.Check(requests, pools);

            var total = marks.Sum();
            if (total < target)
            {
                var failure = Assert.Single(failures);
                Assert.Equal(FailureConstraint.ChapterDistribution, failure.Constraint);
                Assert.Equal(total, failure.AvailableMarks);
                outcomes["short"]++;
            }
            else if (BruteForceCanReach(marks, target))
            {
                Assert.Empty(failures);
                outcomes["ok"]++;
            }
            else
            {
                var failure = Assert.Single(failures);
                Assert.Equal(FailureConstraint.ExactMarks, failure.Constraint);
                Assert.Equal(total, failure.AvailableMarks);
                outcomes["exact"]++;
            }
        }

        Assert.True(outcomes["ok"] >= 50, $"too few reachable cases: {outcomes["ok"]}");
        Assert.True(outcomes["short"] >= 50, $"too few short cases: {outcomes["short"]}");
        Assert.True(outcomes["exact"] >= 50, $"too few exactMarks cases: {outcomes["exact"]}");
    }

    [Fact]
    public void SubsetSum_AgreesWithBruteForceOnEveryTargetOfRandomPools()
    {
        var random = new SplitMix64Random(7);

        for (var caseNumber = 0; caseNumber < 200; caseNumber++)
        {
            var marks = Enumerable.Range(0, random.NextInt(10)).Select(_ => 1 + random.NextInt(8)).ToArray();

            for (var target = 0; target <= marks.Sum() + 2; target++)
            {
                Assert.Equal(BruteForceCanReach(marks, target), SubsetSum.CanReach(marks, target));
            }
        }
    }

    [Fact]
    public void SubsetSum_RejectsNonPositiveMarksAndNegativeTargets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SubsetSum.CanReach([2, 0], 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => SubsetSum.CanReach([2, -1], 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => SubsetSum.CanReach([2], -1));
    }

    private static bool BruteForceCanReach(int[] marks, int target)
    {
        for (var mask = 0; mask < (1 << marks.Length); mask++)
        {
            var sum = 0;
            for (var bit = 0; bit < marks.Length; bit++)
            {
                if ((mask & (1 << bit)) != 0)
                {
                    sum += marks[bit];
                }
            }

            if (sum == target)
            {
                return true;
            }
        }

        return false;
    }
}
