using ExamForge.Domain.Enums;
using ExamForge.Domain.Generation;

namespace ExamForge.Tests.Generation;

public class PaperSolverTests
{
    private static readonly DifficultyMix DefaultMix = new(0.3, 0.5, 0.2);

    private static SolverQuestion Q(int id, int chapterId, int marks, Difficulty difficulty = Difficulty.Medium) =>
        new(id, chapterId, marks, difficulty);

    private static ChapterRequest Request(int chapterId, int orderIndex, int marks) =>
        new(chapterId, $"Chapter {chapterId}", orderIndex, marks);

    private static IReadOnlyDictionary<int, IReadOnlyList<SolverQuestion>> PoolsOf(
        params (int ChapterId, SolverQuestion[] Questions)[] chapters) =>
        chapters.ToDictionary(c => c.ChapterId, c => (IReadOnlyList<SolverQuestion>)c.Questions);

    // A pool with a spread of marks and difficulties, built from the index so it is fixed.
    private static SolverQuestion[] AmplePool(int chapterId, int firstId, int size = 20) =>
        Enumerable.Range(0, size)
            .Select(i => Q(firstId + i, chapterId, 1 + i % 4, TestDifficulty.Cycle(i)))
            .ToArray();

    private static (ChapterRequest[] Requests, IReadOnlyDictionary<int, IReadOnlyList<SolverQuestion>> Pools) AmpleSpec() =>
        (
            [Request(1, 1, 10), Request(2, 2, 12), Request(3, 3, 8)],
            PoolsOf((1, AmplePool(1, 100)), (2, AmplePool(2, 200)), (3, AmplePool(3, 300)))
        );

    private static int[] SelectedIds(SolverResult result) =>
        result.Chapters.SelectMany(c => c.Questions).Select(q => q.Id).ToArray();

    // ---- Determinism ----

    [Fact]
    public void SameSeed_GivesSameSelection()
    {
        var (requests, pools) = AmpleSpec();

        var first = PaperSolver.Solve(requests, pools, DefaultMix, new SplitMix64Random(2026));
        var second = PaperSolver.Solve(requests, pools, DefaultMix, new SplitMix64Random(2026));

        Assert.True(first.IsSuccess);
        Assert.Equal(SelectedIds(first), SelectedIds(second));
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentSelections_OnAnAmplePool()
    {
        var (requests, pools) = AmpleSpec();

        var distinctSelections = Enumerable.Range(1, 20)
            .Select(seed => PaperSolver.Solve(requests, pools, DefaultMix, new SplitMix64Random(seed)))
            .Select(result => string.Join(",", SelectedIds(result).Order()))
            .Distinct()
            .Count();

        Assert.True(distinctSelections >= 10, $"only {distinctSelections} distinct selections from 20 seeds");
    }

    [Fact]
    public void Result_DoesNotDependOnInputPoolOrder()
    {
        var (requests, pools) = AmpleSpec();
        var reversed = pools.ToDictionary(p => p.Key, p => (IReadOnlyList<SolverQuestion>)p.Value.Reverse().ToList());
        var scrambled = pools.ToDictionary(p => p.Key, p =>
        {
            var copy = p.Value.ToList();
            copy.Shuffle(new SplitMix64Random(99));
            return (IReadOnlyList<SolverQuestion>)copy;
        });

        var expected = SelectedIds(PaperSolver.Solve(requests, pools, DefaultMix, new SplitMix64Random(5)));

        Assert.Equal(expected, SelectedIds(PaperSolver.Solve(requests, reversed, DefaultMix, new SplitMix64Random(5))));
        Assert.Equal(expected, SelectedIds(PaperSolver.Solve(requests, scrambled, DefaultMix, new SplitMix64Random(5))));
    }

    [Fact]
    public void Chapters_AreReturnedInOrderIndexOrder_WhateverTheRequestOrder()
    {
        var pools = PoolsOf((1, AmplePool(1, 100)), (2, AmplePool(2, 200)), (3, AmplePool(3, 300)));
        var requests = new[] { Request(3, 3, 5), Request(1, 1, 5), Request(2, 2, 5) };

        var result = PaperSolver.Solve(requests, pools, DefaultMix, new SplitMix64Random(1));

        Assert.Equal([1, 2, 3], result.Chapters.Select(c => c.ChapterId));
    }

    // ---- Selection validity ----

    [Fact]
    public void SelectedQuestions_ComeFromTheirPool_AndNoneIsChosenTwice()
    {
        var (requests, pools) = AmpleSpec();

        for (var seed = 0; seed < 50; seed++)
        {
            var result = PaperSolver.Solve(requests, pools, DefaultMix, new SplitMix64Random(seed));

            Assert.True(result.IsSuccess);
            Assert.Equal(SelectedIds(result).Length, SelectedIds(result).Distinct().Count());
            foreach (var chapter in result.Chapters)
            {
                var poolIds = pools[chapter.ChapterId].Select(q => q.Id).ToHashSet();
                Assert.All(chapter.Questions, q => Assert.Contains(q.Id, poolIds));
            }
        }
    }

    [Fact]
    public void EveryChapter_HitsItsTargetExactly_AcrossManyRandomFeasibleSpecs()
    {
        var specRandom = new SplitMix64Random(20261018);

        for (var caseNumber = 0; caseNumber < 500; caseNumber++)
        {
            var chapterCount = 1 + specRandom.NextInt(4);
            var requests = new List<ChapterRequest>();
            var pools = new Dictionary<int, IReadOnlyList<SolverQuestion>>();
            var nextId = 1;

            for (var chapterId = 1; chapterId <= chapterCount; chapterId++)
            {
                var pool = Enumerable.Range(0, 2 + specRandom.NextInt(14))
                    .Select(_ => Q(nextId++, chapterId, 1 + specRandom.NextInt(5), TestDifficulty.Cycle(specRandom.NextInt(3))))
                    .ToList();

                // The target is the sum of a random non-empty subset, so it is feasible by construction.
                var target = pool.Where(_ => specRandom.NextInt(2) == 0).Sum(q => q.Marks);
                if (target == 0)
                {
                    target = pool[0].Marks;
                }

                requests.Add(Request(chapterId, chapterId, target));
                pools[chapterId] = pool;
            }

            var result = PaperSolver.Solve(requests, pools, DefaultMix, new SplitMix64Random(caseNumber));

            Assert.True(result.IsSuccess, $"case {caseNumber} should be feasible");
            foreach (var request in requests)
            {
                var chapter = Assert.Single(result.Chapters, c => c.ChapterId == request.ChapterId);
                Assert.Equal(request.RequestedMarks, chapter.Questions.Sum(q => q.Marks));
            }

            Assert.Equal(requests.Sum(r => r.RequestedMarks), result.Chapters.Sum(c => c.Questions.Sum(q => q.Marks)));
            Assert.Equal(SelectedIds(result).Length, SelectedIds(result).Distinct().Count());
        }
    }

    [Fact]
    public void AchievedMix_MatchesTheQuestionsActuallySelected()
    {
        var (requests, pools) = AmpleSpec();

        var result = PaperSolver.Solve(requests, pools, DefaultMix, new SplitMix64Random(3));

        var selected = result.Chapters.SelectMany(c => c.Questions).ToList();
        var total = (double)selected.Sum(q => q.Marks);
        Assert.NotNull(result.AchievedMix);
        Assert.Equal(selected.Where(q => q.Difficulty == Difficulty.Easy).Sum(q => q.Marks) / total, result.AchievedMix.Easy, 9);
        Assert.Equal(selected.Where(q => q.Difficulty == Difficulty.Medium).Sum(q => q.Marks) / total, result.AchievedMix.Medium, 9);
        Assert.Equal(selected.Where(q => q.Difficulty == Difficulty.Hard).Sum(q => q.Marks) / total, result.AchievedMix.Hard, 9);
    }

    // ---- Honest failure ----

    [Fact]
    public void InfeasibleSpec_ReturnsFailures_AndNoSelection()
    {
        var requests = new[] { Request(1, 1, 50), Request(2, 2, 9) };
        var pools = PoolsOf((1, AmplePool(1, 100, 3)), (2, [Q(1, 2, 2), Q(2, 2, 4), Q(3, 2, 6)]));

        var result = PaperSolver.Solve(requests, pools, DefaultMix, new SplitMix64Random(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Failures.Count);
        Assert.Empty(result.Chapters);
        Assert.Null(result.AchievedMix);
    }

    [Fact]
    public void InfeasibleSpecs_NeverReturnASelection_AcrossManyRandomSpecs()
    {
        var specRandom = new SplitMix64Random(31);

        for (var caseNumber = 0; caseNumber < 300; caseNumber++)
        {
            var marks = Enumerable.Range(0, specRandom.NextInt(8)).Select(_ => 2 * (1 + specRandom.NextInt(3))).ToArray();
            var pool = marks.Select((m, i) => Q(i + 1, 1, m)).ToArray();
            // Even marks only: any odd target is infeasible; any target above the total is infeasible.
            var target = specRandom.NextInt(2) == 0 ? 2 * specRandom.NextInt(10) + 1 : marks.Sum() + 1 + specRandom.NextInt(5);

            var result = PaperSolver.Solve([Request(1, 1, target)], PoolsOf((1, pool)), DefaultMix, new SplitMix64Random(caseNumber));

            Assert.False(result.IsSuccess);
            Assert.Empty(result.Chapters);
            Assert.NotEmpty(result.Failures);
        }
    }

    // ---- Difficulty mix ----

    [Theory]
    [InlineData(0, 0, 1, new[] { 3, 4 })]   // all hard wanted -> the two hard questions
    [InlineData(1, 0, 0, new[] { 1, 2 })]   // all easy wanted -> the two easy questions
    public void WhenOneExactCombinationMatchesTheMixBetter_ItWins(double easy, double medium, double hard, int[] expectedIds)
    {
        var pool = new[]
        {
            Q(1, 1, 2, Difficulty.Easy), Q(2, 1, 2, Difficulty.Easy),
            Q(3, 1, 2, Difficulty.Hard), Q(4, 1, 2, Difficulty.Hard),
        };

        for (var seed = 0; seed < 20; seed++)
        {
            var result = PaperSolver.Solve([Request(1, 1, 4)], PoolsOf((1, pool)), new DifficultyMix(easy, medium, hard), new SplitMix64Random(seed));

            Assert.Equal(expectedIds, SelectedIds(result).Order());
        }
    }

    [Fact]
    public void SecondChapter_CompensatesForTheFirstChaptersMix()
    {
        // Chapter 1 has no choice: 4 marks of Easy. To land on 50% easy / 50% hard overall,
        // chapter 2 must be all Hard. Judged alone, chapter 2 would prefer one Easy + one Hard
        // (a 50/50 chapter), so this fails if the mix is not cumulative.
        var pools = PoolsOf(
            (1, [Q(1, 1, 2, Difficulty.Easy), Q(2, 1, 2, Difficulty.Easy)]),
            (2, [
                Q(10, 2, 2, Difficulty.Easy), Q(11, 2, 2, Difficulty.Easy),
                Q(12, 2, 2, Difficulty.Medium), Q(13, 2, 2, Difficulty.Medium),
                Q(14, 2, 2, Difficulty.Hard), Q(15, 2, 2, Difficulty.Hard),
            ]));

        for (var seed = 0; seed < 20; seed++)
        {
            var result = PaperSolver.Solve(
                [Request(1, 1, 4), Request(2, 2, 4)], pools, new DifficultyMix(0.5, 0, 0.5), new SplitMix64Random(seed));

            Assert.Equal([14, 15], result.Chapters[1].Questions.Select(q => q.Id).Order());
            Assert.Equal(0.5, result.AchievedMix!.Easy, 9);
            Assert.Equal(0.5, result.AchievedMix.Hard, 9);
        }
    }

    [Fact]
    public void WhenCombinationsTieOnTheMix_TheFirstFoundWins()
    {
        // Every question is identical in marks and difficulty, so every combination ties.
        // The walk takes before it skips, so the first combination found is the first three
        // questions of the shuffled (Id-sorted) pool.
        var pool = Enumerable.Range(1, 10).Select(id => Q(id, 1, 1)).ToList();
        var expectedOrder = pool.ToList();
        expectedOrder.Shuffle(new SplitMix64Random(77));

        var result = PaperSolver.Solve([Request(1, 1, 3)], PoolsOf((1, pool.ToArray())), DefaultMix, new SplitMix64Random(77));

        Assert.Equal(expectedOrder.Take(3).Select(q => q.Id), result.Chapters[0].Questions.Select(q => q.Id));
    }

    // ---- Invariant ----

    [Fact]
    public void VerifySelection_ThrowsWhenAChapterIsShort()
    {
        var requests = new[] { Request(1, 1, 5) };
        var shortSelection = new[] { new ChapterSelection(1, [Q(1, 1, 2), Q(2, 1, 2)]) };

        Assert.Throws<InvalidOperationException>(() => PaperSolver.VerifySelection(requests, shortSelection));
    }

    [Fact]
    public void VerifySelection_ThrowsWhenAChapterIsMissing()
    {
        var requests = new[] { Request(1, 1, 4), Request(2, 2, 4) };
        var selection = new[] { new ChapterSelection(1, [Q(1, 1, 4)]) };

        Assert.Throws<InvalidOperationException>(() => PaperSolver.VerifySelection(requests, selection));
    }

    [Fact]
    public void VerifySelection_AcceptsExactSelections()
    {
        var requests = new[] { Request(1, 1, 4) };
        var selection = new[] { new ChapterSelection(1, [Q(1, 1, 1), Q(2, 1, 3)]) };

        PaperSolver.VerifySelection(requests, selection);
    }

    // ---- Pruning table ----

    [Fact]
    public void SuffixTable_MatchesBruteForceOnEveryCell()
    {
        var random = new SplitMix64Random(11);

        for (var caseNumber = 0; caseNumber < 100; caseNumber++)
        {
            var marks = Enumerable.Range(0, random.NextInt(9)).Select(_ => 1 + random.NextInt(6)).ToArray();
            var target = random.NextInt(25);

            var table = SubsetSum.SuffixTable(marks, target);

            Assert.Equal(marks.Length + 1, table.Length);
            for (var i = 0; i <= marks.Length; i++)
            {
                var suffix = marks.Skip(i).ToArray();
                for (var sum = 0; sum <= target; sum++)
                {
                    Assert.Equal(BruteForce(suffix, sum), table[i][sum]);
                }
            }
        }
    }

    private static bool BruteForce(int[] marks, int target)
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
