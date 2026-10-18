using ExamForge.Domain.Enums;
using ExamForge.Domain.Generation;

namespace ExamForge.Tests.Generation;

// Pruning and the combination cap change only how long the search takes, never what a correct
// answer looks like, so correctness tests cannot see them. These run the solver on a separate
// task with a timeout: a search that explodes fails the test instead of freezing the run.
public class SolverSearchBoundsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private static readonly DifficultyMix Mix = new(0.3, 0.5, 0.2);

    private static async Task<SolverResult> SolveWithinTimeout(
        int target, IReadOnlyList<SolverQuestion> pool, int seed)
    {
        var request = new ChapterRequest(1, "Chapter 1", 1, target);
        var pools = new Dictionary<int, IReadOnlyList<SolverQuestion>> { [1] = pool };

        var task = Task.Run(() => PaperSolver.Solve([request], pools, Mix, new SplitMix64Random(seed)));

        var finished = await Task.WhenAny(task, Task.Delay(Timeout));
        Assert.True(finished == task, $"solver did not finish within {Timeout.TotalSeconds}s (seed {seed})");
        return await task;
    }

    [Fact]
    public void CombinationCap_IsSixtyFour()
    {
        Assert.Equal(64, PaperSolver.MaxCombinationsPerChapter);
    }

    // 50 two-mark questions and one 3-mark question. 101 is odd, so every solution contains the
    // 3-mark question plus 49 of the 50 two-mark ones: exactly 50 solutions, fewer than the cap,
    // so the cap cannot end the search early. The walk must therefore refuse the "skip the
    // 3-mark question" branch, which is a dead end holding about 2^50 subsets of two-mark
    // questions. Only the pruning table keeps it out of that branch.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Pruning_KeepsTheSearchOutOfDeadEnds(int seed)
    {
        var threeMark = new SolverQuestion(1000, 1, 3, Difficulty.Medium);
        var pool = Enumerable.Range(1, 50)
            .Select(id => new SolverQuestion(id, 1, 2, TestDifficulty.Cycle(id)))
            .Append(threeMark)
            .ToList();

        var result = await SolveWithinTimeout(101, pool, seed);

        Assert.True(result.IsSuccess);
        var questions = result.Chapters[0].Questions;
        Assert.Equal(101, questions.Sum(q => q.Marks));
        Assert.Contains(questions, q => q.Id == 1000);
    }

    // 40 one-mark questions and target 20: C(40,20), about 1.4 * 10^11 exact combinations. Only the
    // cap lets the walk stop after 64 of them.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CombinationCap_StopsTheSearchOnAHugeSolutionSpace(int seed)
    {
        var pool = Enumerable.Range(1, 40)
            .Select(id => new SolverQuestion(id, 1, 1, TestDifficulty.Cycle(id)))
            .ToList();

        var result = await SolveWithinTimeout(20, pool, seed);

        Assert.True(result.IsSuccess);
        var questions = result.Chapters[0].Questions;
        Assert.Equal(20, questions.Sum(q => q.Marks));
        Assert.Equal(20, questions.Select(q => q.Id).Distinct().Count());
    }
}
