using ExamForge.Domain.Enums;

namespace ExamForge.Domain.Generation;

/// <summary>
/// Pure selection: in-memory pools in, selection or failures out. No database, no clock,
/// no global random; all randomness comes from the supplied IRandomSource.
/// </summary>
public static class PaperSolver
{
    /// <summary>How many exact-sum combinations are collected per chapter before choosing the best.</summary>
    public const int MaxCombinationsPerChapter = 64;

    public static SolverResult Solve(
        IReadOnlyList<ChapterRequest> requests,
        IReadOnlyDictionary<int, IReadOnlyList<SolverQuestion>> pools,
        DifficultyMix requestedMix,
        IRandomSource random)
    {
        var failures = FeasibilityChecker.Check(requests, pools);
        if (failures.Count > 0)
        {
            return SolverResult.Failed(failures);
        }

        // Marks chosen so far per difficulty, indexed by Slot(): Easy, Medium, Hard.
        var cumulative = new int[3];
        var chapters = new List<ChapterSelection>();

        foreach (var request in requests.OrderBy(r => r.OrderIndex))
        {
            var pool = pools.TryGetValue(request.ChapterId, out var found) ? found : [];
            var chosen = SelectForChapter(request, pool, cumulative, requestedMix, random);

            chapters.Add(new ChapterSelection(request.ChapterId, chosen));
            foreach (var question in chosen)
            {
                cumulative[Slot(question.Difficulty)] += question.Marks;
            }
        }

        VerifySelection(requests, chapters);

        var ordered = PaperOrdering.Order(requests, chapters);
        VerifyOrderedPaper(requests, ordered);

        // Measured on the ordered list, so the reported mix describes exactly what is returned.
        var achieved = DifficultyMix.FromMarks(
            MarksOf(ordered, Difficulty.Easy), MarksOf(ordered, Difficulty.Medium), MarksOf(ordered, Difficulty.Hard));

        return SolverResult.Succeeded(chapters, ordered, achieved);
    }

    /// <summary>
    /// Final check on the finished list: OrderIndex is exactly 1..n, and the marks add up to the
    /// requested total and to each chapter's target. A violation is a bug, never a short paper.
    /// </summary>
    public static void VerifyOrderedPaper(IReadOnlyList<ChapterRequest> requests, IReadOnlyList<OrderedQuestion> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].OrderIndex != i + 1)
            {
                throw new InvalidOperationException(
                    $"Solver bug: line {i + 1} has OrderIndex {ordered[i].OrderIndex}.");
            }
        }

        var expectedTotal = requests.Sum(r => r.RequestedMarks);
        var actualTotal = ordered.Sum(o => o.Marks);
        if (actualTotal != expectedTotal)
        {
            throw new InvalidOperationException(
                $"Solver bug: paper has {actualTotal} marks, expected {expectedTotal}.");
        }

        foreach (var request in requests)
        {
            var chapterMarks = ordered.Where(o => o.ChapterId == request.ChapterId).Sum(o => o.Marks);
            if (chapterMarks != request.RequestedMarks)
            {
                throw new InvalidOperationException(
                    $"Solver bug: chapter {request.ChapterId} has {chapterMarks} marks, expected {request.RequestedMarks}.");
            }
        }
    }

    private static int MarksOf(IEnumerable<OrderedQuestion> ordered, Difficulty difficulty) =>
        ordered.Where(o => o.Difficulty == difficulty).Sum(o => o.Marks);

    // Position of a difficulty in the cumulative tally. A switch rather than a cast, because the
    // enum's numeric values are for sorting and are not array indexes.
    private static int Slot(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => 0,
        Difficulty.Medium => 1,
        Difficulty.Hard => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Unknown difficulty."),
    };

    /// <summary>
    /// The last line of defence: every chapter's selected marks must equal its target. A
    /// violation is a bug in the solver and must never surface as a short paper.
    /// </summary>
    public static void VerifySelection(IReadOnlyList<ChapterRequest> requests, IReadOnlyList<ChapterSelection> chapters)
    {
        foreach (var request in requests)
        {
            var chapter = chapters.FirstOrDefault(c => c.ChapterId == request.ChapterId)
                ?? throw new InvalidOperationException(
                    $"Solver bug: chapter {request.ChapterId} has no selection.");

            var selected = chapter.Questions.Sum(q => q.Marks);
            if (selected != request.RequestedMarks)
            {
                throw new InvalidOperationException(
                    $"Solver bug: chapter {request.ChapterId} selected {selected} marks, expected {request.RequestedMarks}.");
            }
        }
    }

    private static IReadOnlyList<SolverQuestion> SelectForChapter(
        ChapterRequest request,
        IReadOnlyList<SolverQuestion> pool,
        int[] cumulative,
        DifficultyMix requestedMix,
        IRandomSource random)
    {
        // Sort by Id first so the caller's order never matters, then shuffle with the shared source.
        var ordered = pool.OrderBy(q => q.Id).ToList();
        ordered.Shuffle(random);

        var target = request.RequestedMarks;
        var table = SubsetSum.SuffixTable(ordered.Select(q => q.Marks).ToList(), target);

        var combinations = new List<SolverQuestion[]>();
        var current = new List<SolverQuestion>();

        // Depth-first: at each question, take it, then skip it. A branch is entered only when the
        // table says the rest of the pool can still reach the remaining marks, so the walk never
        // dead-ends. Marks are positive, so each exact set of questions is found exactly once.
        void Walk(int index, int remaining)
        {
            if (remaining == 0)
            {
                combinations.Add(current.ToArray());
                return;
            }

            var question = ordered[index];

            if (remaining >= question.Marks && table[index + 1][remaining - question.Marks])
            {
                current.Add(question);
                Walk(index + 1, remaining - question.Marks);
                current.RemoveAt(current.Count - 1);

                if (combinations.Count >= MaxCombinationsPerChapter)
                {
                    return;
                }
            }

            if (table[index + 1][remaining])
            {
                Walk(index + 1, remaining);
            }
        }

        // Feasibility already proved table[0][target] is true.
        Walk(0, target);

        // Pick the combination whose cumulative mix (earlier chapters plus this one) is closest to
        // the request. Strictly-less keeps the first one found on ties.
        SolverQuestion[]? best = null;
        var bestDistance = double.MaxValue;

        foreach (var combination in combinations)
        {
            var easy = cumulative[0] + combination.Where(q => q.Difficulty == Difficulty.Easy).Sum(q => q.Marks);
            var medium = cumulative[1] + combination.Where(q => q.Difficulty == Difficulty.Medium).Sum(q => q.Marks);
            var hard = cumulative[2] + combination.Where(q => q.Difficulty == Difficulty.Hard).Sum(q => q.Marks);

            var distance = DifficultyMix.FromMarks(easy, medium, hard).L1Distance(requestedMix);
            if (distance < bestDistance)
            {
                best = combination;
                bestDistance = distance;
            }
        }

        return best ?? throw new InvalidOperationException(
            $"Solver bug: no combination found for chapter {request.ChapterId} although it was feasible.");
    }
}
