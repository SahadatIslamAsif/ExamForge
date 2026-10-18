using ExamForge.Domain.Enums;
using ExamForge.Domain.Generation;

namespace ExamForge.Tests.Generation;

public class PaperOrderingTests
{
    private static readonly DifficultyMix Mix = new(0.3, 0.5, 0.2);

    private static SolverQuestion Q(int id, int chapterId, int marks, Difficulty difficulty) =>
        new(id, chapterId, marks, difficulty);

    private static ChapterRequest Request(int chapterId, int orderIndex, int marks) =>
        new(chapterId, $"Chapter {chapterId}", orderIndex, marks);

    private static int[] Ids(IEnumerable<OrderedQuestion> ordered) => ordered.Select(o => o.QuestionId).ToArray();

    // ---- The enum ----

    [Fact]
    public void Difficulty_HasExplicitValues_AndSortsEasyToHard()
    {
        Assert.Equal(1, (int)Difficulty.Easy);
        Assert.Equal(2, (int)Difficulty.Medium);
        Assert.Equal(3, (int)Difficulty.Hard);
    }

    // ---- Order rule, tested directly on hand-built selections ----

    [Fact]
    public void Chapters_FollowOrderIndex_NotChapterId()
    {
        // Chapter ids 30, 10, 20 are deliberately not in OrderIndex order.
        var requests = new[] { Request(10, 2, 1), Request(20, 3, 1), Request(30, 1, 1) };
        var chapters = new[]
        {
            new ChapterSelection(10, [Q(2, 10, 1, Difficulty.Easy)]),
            new ChapterSelection(20, [Q(3, 20, 1, Difficulty.Easy)]),
            new ChapterSelection(30, [Q(1, 30, 1, Difficulty.Easy)]),
        };

        var ordered = PaperOrdering.Order(requests, chapters);

        Assert.Equal([30, 10, 20], ordered.Select(o => o.ChapterId));
        Assert.Equal([1, 2, 3], Ids(ordered));
    }

    [Fact]
    public void WithinAChapter_DifficultyIsAscending()
    {
        var requests = new[] { Request(1, 1, 6) };
        var chapters = new[]
        {
            new ChapterSelection(1,
            [
                Q(1, 1, 2, Difficulty.Hard),
                Q(2, 1, 2, Difficulty.Easy),
                Q(3, 1, 2, Difficulty.Medium),
            ]),
        };

        var ordered = PaperOrdering.Order(requests, chapters);

        Assert.Equal([Difficulty.Easy, Difficulty.Medium, Difficulty.Hard], ordered.Select(o => o.Difficulty));
        Assert.Equal([2, 3, 1], Ids(ordered));
    }

    [Fact]
    public void WhenDifficultyTies_QuestionIdBreaksIt()
    {
        // Supplied out of Id order, so a stable sort that ignores Id would keep 5, 3, 9.
        var requests = new[] { Request(1, 1, 3) };
        var chapters = new[]
        {
            new ChapterSelection(1,
            [
                Q(5, 1, 1, Difficulty.Medium),
                Q(3, 1, 1, Difficulty.Medium),
                Q(9, 1, 1, Difficulty.Medium),
            ]),
        };

        Assert.Equal([3, 5, 9], Ids(PaperOrdering.Order(requests, chapters)));
    }

    [Fact]
    public void DifficultyOutranksId_AndChapterOutranksDifficulty()
    {
        var requests = new[] { Request(1, 1, 2), Request(2, 2, 2) };
        var chapters = new[]
        {
            new ChapterSelection(2, [Q(1, 2, 1, Difficulty.Easy), Q(2, 2, 1, Difficulty.Easy)]),
            new ChapterSelection(1, [Q(8, 1, 1, Difficulty.Hard), Q(9, 1, 1, Difficulty.Hard)]),
        };

        // Chapter 1 comes first even though its questions are harder and have larger Ids.
        Assert.Equal([8, 9, 1, 2], Ids(PaperOrdering.Order(requests, chapters)));
    }

    [Fact]
    public void OrderIndex_IsOneBasedWithNoGaps()
    {
        var requests = new[] { Request(1, 1, 3), Request(2, 2, 3) };
        var chapters = new[]
        {
            new ChapterSelection(1, [Q(1, 1, 1, Difficulty.Hard), Q(2, 1, 1, Difficulty.Easy), Q(3, 1, 1, Difficulty.Medium)]),
            new ChapterSelection(2, [Q(4, 2, 1, Difficulty.Easy), Q(5, 2, 1, Difficulty.Easy), Q(6, 2, 1, Difficulty.Hard)]),
        };

        var ordered = PaperOrdering.Order(requests, chapters);

        Assert.Equal(Enumerable.Range(1, 6), ordered.Select(o => o.OrderIndex));
    }

    [Fact]
    public void Lines_CarryTheQuestionAndChapterDetails()
    {
        var requests = new[] { new ChapterRequest(7, "Energy", 1, 3) };
        var chapters = new[] { new ChapterSelection(7, [Q(42, 7, 3, Difficulty.Hard)]) };

        var line = Assert.Single(PaperOrdering.Order(requests, chapters));

        Assert.Equal(new OrderedQuestion(1, 42, 7, "Energy", 3, Difficulty.Hard), line);
    }

    [Fact]
    public void Order_DoesNotDependOnTheOrderOfTheInput()
    {
        var requests = new[] { Request(10, 2, 6), Request(20, 3, 6), Request(30, 1, 6) };
        var chapters = new[]
        {
            new ChapterSelection(10, [Q(1, 10, 2, Difficulty.Hard), Q(2, 10, 2, Difficulty.Easy), Q(3, 10, 2, Difficulty.Easy)]),
            new ChapterSelection(20, [Q(4, 20, 2, Difficulty.Medium), Q(5, 20, 2, Difficulty.Medium), Q(6, 20, 2, Difficulty.Hard)]),
            new ChapterSelection(30, [Q(7, 30, 2, Difficulty.Easy), Q(8, 30, 2, Difficulty.Hard), Q(9, 30, 2, Difficulty.Medium)]),
        };
        var expected = Ids(PaperOrdering.Order(requests, chapters));

        for (var seed = 0; seed < 50; seed++)
        {
            var random = new SplitMix64Random(seed);
            var shuffledChapters = chapters
                .Select(c => new ChapterSelection(c.ChapterId, c.Questions.ToList().Also(l => l.Shuffle(random))))
                .ToList();
            shuffledChapters.Shuffle(random);
            var shuffledRequests = requests.ToList();
            shuffledRequests.Shuffle(random);

            Assert.Equal(expected, Ids(PaperOrdering.Order(shuffledRequests, shuffledChapters)));
        }
    }

    // ---- The ordered list on a solver result ----

    private static SolverQuestion[] AmplePool(int chapterId, int firstId) =>
        Enumerable.Range(0, 20)
            .Select(i => Q(firstId + i, chapterId, 1 + i % 4, TestDifficulty.Cycle(i)))
            .ToArray();

    [Fact]
    public void SolverResult_OrderedListHoldsExactlyTheSelectedQuestions()
    {
        var requests = new[] { Request(3, 2, 10), Request(1, 1, 12), Request(2, 3, 8) };
        var pools = new Dictionary<int, IReadOnlyList<SolverQuestion>>
        {
            [1] = AmplePool(1, 100),
            [2] = AmplePool(2, 200),
            [3] = AmplePool(3, 300),
        };

        var result = PaperSolver.Solve(requests, pools, Mix, new SplitMix64Random(8));

        Assert.True(result.IsSuccess);
        var selectedIds = result.Chapters.SelectMany(c => c.Questions).Select(q => q.Id).Order().ToArray();
        Assert.Equal(selectedIds, Ids(result.OrderedQuestions).Order());
        Assert.Equal(30, result.OrderedQuestions.Sum(o => o.Marks));
        Assert.Equal([1, 3, 2], result.OrderedQuestions.Select(o => o.ChapterId).Distinct());
    }

    [Fact]
    public void SolverResult_AchievedMixMatchesTheOrderedList()
    {
        var requests = new[] { Request(1, 1, 12), Request(2, 2, 8) };
        var pools = new Dictionary<int, IReadOnlyList<SolverQuestion>>
        {
            [1] = AmplePool(1, 100),
            [2] = AmplePool(2, 200),
        };

        var result = PaperSolver.Solve(requests, pools, Mix, new SplitMix64Random(2));

        var total = (double)result.OrderedQuestions.Sum(o => o.Marks);
        double Share(Difficulty d) => result.OrderedQuestions.Where(o => o.Difficulty == d).Sum(o => o.Marks) / total;
        Assert.Equal(Share(Difficulty.Easy), result.AchievedMix!.Easy, 9);
        Assert.Equal(Share(Difficulty.Medium), result.AchievedMix.Medium, 9);
        Assert.Equal(Share(Difficulty.Hard), result.AchievedMix.Hard, 9);
    }

    [Fact]
    public void WhenOnlyOneSelectionExists_TheOrderingIsTheSameForEverySeedAndInputOrder()
    {
        // Target equals the pool's total, so the whole pool is the only possible selection and
        // only the ordering rule is left to vary.
        var pool = new[]
        {
            Q(7, 5, 2, Difficulty.Hard), Q(3, 5, 2, Difficulty.Easy), Q(9, 5, 2, Difficulty.Medium),
            Q(1, 5, 2, Difficulty.Easy), Q(4, 5, 2, Difficulty.Hard),
        };
        var requests = new[] { Request(5, 1, 10) };

        List<int>? expected = null;
        for (var seed = 0; seed < 30; seed++)
        {
            var shuffledPool = pool.ToList();
            shuffledPool.Shuffle(new SplitMix64Random(seed + 1000));
            var pools = new Dictionary<int, IReadOnlyList<SolverQuestion>> { [5] = shuffledPool };

            var result = PaperSolver.Solve(requests, pools, Mix, new SplitMix64Random(seed));

            expected ??= Ids(result.OrderedQuestions).ToList();
            Assert.Equal(expected, Ids(result.OrderedQuestions));
        }

        Assert.Equal([1, 3, 9, 4, 7], expected);
    }

    [Fact]
    public void FailedResult_HasNoOrderedQuestions()
    {
        var result = PaperSolver.Solve(
            [Request(1, 1, 50)],
            new Dictionary<int, IReadOnlyList<SolverQuestion>> { [1] = AmplePool(1, 100).Take(2).ToArray() },
            Mix,
            new SplitMix64Random(1));

        Assert.False(result.IsSuccess);
        Assert.Empty(result.OrderedQuestions);
    }

    [Fact]
    public void RandomFeasibleSpecs_ProduceACorrectlyOrderedExactPaper()
    {
        var specRandom = new SplitMix64Random(20261018);

        for (var caseNumber = 0; caseNumber < 500; caseNumber++)
        {
            var chapterCount = 1 + specRandom.NextInt(4);

            // Chapter ids and OrderIndexes come from separate shuffles so the two never agree by luck.
            var chapterIds = Enumerable.Range(1, 9).Select(i => i * 7).ToList();
            chapterIds.Shuffle(specRandom);
            var orderIndexes = Enumerable.Range(1, chapterCount).ToList();
            orderIndexes.Shuffle(specRandom);

            var requests = new List<ChapterRequest>();
            var pools = new Dictionary<int, IReadOnlyList<SolverQuestion>>();
            var nextId = 1;
            for (var c = 0; c < chapterCount; c++)
            {
                var chapterId = chapterIds[c];
                var pool = Enumerable.Range(0, 2 + specRandom.NextInt(14))
                    .Select(_ => Q(nextId++, chapterId, 1 + specRandom.NextInt(5), TestDifficulty.Cycle(specRandom.NextInt(3))))
                    .ToList();
                var target = pool.Where(_ => specRandom.NextInt(2) == 0).Sum(q => q.Marks);
                if (target == 0)
                {
                    target = pool[0].Marks;
                }

                requests.Add(Request(chapterId, orderIndexes[c], target));
                pools[chapterId] = pool;
            }

            var result = PaperSolver.Solve(requests, pools, Mix, new SplitMix64Random(caseNumber));
            Assert.True(result.IsSuccess, $"case {caseNumber} should be feasible");
            var ordered = result.OrderedQuestions;

            // OrderIndex is exactly 1..n.
            Assert.Equal(Enumerable.Range(1, ordered.Count), ordered.Select(o => o.OrderIndex));

            // Strictly increasing on (chapter OrderIndex, difficulty, question Id): a total order, no ties.
            var chapterOrder = requests.ToDictionary(r => r.ChapterId, r => r.OrderIndex);
            var keys = ordered.Select(o => (chapterOrder[o.ChapterId], (int)o.Difficulty, o.QuestionId)).ToList();
            for (var i = 1; i < keys.Count; i++)
            {
                Assert.True(keys[i - 1].CompareTo(keys[i]) < 0, $"case {caseNumber}: lines {i} and {i + 1} are out of order");
            }

            // Exactly the selected questions, with their chapter, marks and difficulty.
            var selected = result.Chapters.SelectMany(c => c.Questions).OrderBy(q => q.Id).ToList();
            Assert.Equal(
                selected.Select(q => (q.Id, q.ChapterId, q.Marks, q.Difficulty)),
                ordered.OrderBy(o => o.QuestionId).Select(o => (o.QuestionId, o.ChapterId, o.Marks, o.Difficulty)));

            // Marks are exact, overall and per chapter.
            Assert.Equal(requests.Sum(r => r.RequestedMarks), ordered.Sum(o => o.Marks));
            foreach (var request in requests)
            {
                Assert.Equal(request.RequestedMarks, ordered.Where(o => o.ChapterId == request.ChapterId).Sum(o => o.Marks));
            }

            // The achieved mix describes the ordered list.
            var total = (double)ordered.Sum(o => o.Marks);
            Assert.Equal(ordered.Where(o => o.Difficulty == Difficulty.Hard).Sum(o => o.Marks) / total, result.AchievedMix!.Hard, 9);
        }
    }

    // ---- Final check on the ordered list ----

    [Fact]
    public void VerifyOrderedPaper_AcceptsAnExactPaper()
    {
        var ordered = new[]
        {
            new OrderedQuestion(1, 1, 1, "A", 2, Difficulty.Easy),
            new OrderedQuestion(2, 2, 1, "A", 3, Difficulty.Hard),
        };

        PaperSolver.VerifyOrderedPaper([Request(1, 1, 5)], ordered);
    }

    [Fact]
    public void VerifyOrderedPaper_ThrowsWhenTotalMarksAreShort()
    {
        var ordered = new[] { new OrderedQuestion(1, 1, 1, "A", 2, Difficulty.Easy) };

        Assert.Throws<InvalidOperationException>(() => PaperSolver.VerifyOrderedPaper([Request(1, 1, 5)], ordered));
    }

    [Fact]
    public void VerifyOrderedPaper_ThrowsWhenOrderIndexHasAGap()
    {
        var ordered = new[]
        {
            new OrderedQuestion(1, 1, 1, "A", 2, Difficulty.Easy),
            new OrderedQuestion(3, 2, 1, "A", 3, Difficulty.Hard),
        };

        Assert.Throws<InvalidOperationException>(() => PaperSolver.VerifyOrderedPaper([Request(1, 1, 5)], ordered));
    }
}

internal static class TestExtensions
{
    /// <summary>Runs an action on the value and returns it, so a shuffle can sit inside an expression.</summary>
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
