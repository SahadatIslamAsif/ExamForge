using System.Text.Json;
using ExamForge.Domain.Entities;
using ExamForge.Domain.Generation;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Infrastructure.Generation;

/// <summary>
/// Connects the pure solver to Postgres: load the subject, chapters, history and pools, solve,
/// then save the paper and touch the Subject row in one SaveChanges. Concurrency is optimistic:
/// no lock is held while solving; a conflicting commit is detected by Subject's xmin token,
/// and the whole attempt is redone with fresh data (see "Concurrency" in docs/SPEC.md).
/// </summary>
public sealed class PaperGenerationService(
    IDbContextFactory<ExamForgeDbContext> contextFactory,
    TimeProvider timeProvider,
    ISeedSource seedSource) : IPaperGenerationService
{
    public const int MaxAttempts = 3;

    // camelCase, like the API's request body. SpecJson is this canonical serialisation of the
    // specification (with the resolved seed), not the raw HTTP body.
    private static readonly JsonSerializerOptions SpecJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GenerationResult> GenerateAsync(
        PaperSpecification specification,
        CancellationToken cancellationToken = default)
    {
        EnsureConsistent(specification);

        // The one place non-seeded randomness is allowed, before any solving. Every attempt
        // below reuses this seed, so a retry differs only by the fresher history it loads.
        var seed = specification.Seed ?? seedSource.NextSeed();
        var resolved = specification with { Seed = seed };

        for (var attempt = 1; ; attempt++)
        {
            // A fresh context per attempt, so nothing loaded or tracked by a failed attempt survives.
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                return await AttemptAsync(context, resolved, seed, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Another generation for this subject committed after this attempt loaded its
                // history. That paper is part of the history now: load everything again.
            }
            catch (DbUpdateConcurrencyException)
            {
                return new GenerationResult.Conflict(attempt);
            }
        }
    }

    private async Task<GenerationResult> AttemptAsync(
        ExamForgeDbContext context,
        PaperSpecification specification,
        long seed,
        CancellationToken cancellationToken)
    {
        // Tracked, unlike every other read: its Version (xmin) is what the final UPDATE is
        // checked against. A lookup by primary key returns at most one row, so it needs no ORDER BY.
        var subject = await context.Subjects.SingleOrDefaultAsync(s => s.Id == specification.SubjectId, cancellationToken);
        if (subject is null)
        {
            return new GenerationResult.SubjectNotFound(specification.SubjectId);
        }

        var chapterIds = specification.ChapterDistribution.Select(c => c.ChapterId).ToList();
        var chapters = await context.Chapters.AsNoTracking()
            .Where(c => c.SubjectId == subject.Id && chapterIds.Contains(c.Id))
            .OrderBy(c => c.OrderIndex).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Name, c.OrderIndex })
            .ToListAsync(cancellationToken);

        // Ownership needs the database, so the API cannot check it up front. Report it before any
        // history or pool is loaded, and long before solving.
        if (chapters.Count != chapterIds.Count)
        {
            var owned = chapters.Select(c => c.Id).ToHashSet();
            return new GenerationResult.ChaptersNotInSubject(chapterIds.Where(id => !owned.Contains(id)).Order().ToList());
        }

        var history = await LoadHistoryAsync(context, subject.Id, specification.ExcludeLastNPapers, cancellationToken);

        // All active questions of the requested chapters, in Id order. The history split happens
        // in memory below, which keeps the order and also yields the marks the window removed.
        var questions = await context.Questions.AsNoTracking()
            .Where(q => chapterIds.Contains(q.ChapterId) && q.IsActive)
            .OrderBy(q => q.Id)
            .Select(q => new { q.Id, q.ChapterId, q.Marks, q.Difficulty, q.Text })
            .ToListAsync(cancellationToken);

        var marksByChapter = specification.ChapterDistribution.ToDictionary(c => c.ChapterId, c => c.Marks);
        var requests = chapters
            .Select(c => new ChapterRequest(c.Id, c.Name, c.OrderIndex, marksByChapter[c.Id]))
            .ToList();

        var pools = chapters.ToDictionary(
            c => c.Id,
            c => (IReadOnlyList<SolverQuestion>)questions
                .Where(q => q.ChapterId == c.Id && !history.Contains(q.Id))
                .Select(q => new SolverQuestion(q.Id, q.ChapterId, q.Marks, q.Difficulty))
                .ToList());

        var solved = PaperSolver.Solve(requests, pools, specification.DifficultyMix, new SplitMix64Random(seed));

        if (!solved.IsSuccess)
        {
            // Nothing has been added to the context, so nothing is saved.
            var excludedMarks = questions
                .Where(q => history.Contains(q.Id))
                .GroupBy(q => q.ChapterId)
                .ToDictionary(g => g.Key, g => g.Sum(q => q.Marks));

            return new GenerationResult.Infeasible(solved.Failures
                .Select(f => new InfeasibleChapter(f, excludedMarks.GetValueOrDefault(f.ChapterId)))
                .ToList());
        }

        // The solver already verified this; checked again here because a short paper must
        // never be saved, whatever changes in the solver later.
        if (solved.OrderedQuestions.Sum(q => q.Marks) != specification.TotalMarks)
        {
            throw new InvalidOperationException(
                $"Generation bug: selected marks do not equal the requested total {specification.TotalMarks}.");
        }

        var now = TruncateToMicroseconds(timeProvider.GetUtcNow());

        var paper = new Paper
        {
            SubjectId = subject.Id,
            Title = specification.Title,
            TotalMarks = specification.TotalMarks,
            Seed = seed,
            SpecJson = JsonSerializer.Serialize(specification, SpecJsonOptions),
            CreatedAt = now,
            PaperQuestions = solved.OrderedQuestions
                .Select(q => new PaperQuestion { QuestionId = q.QuestionId, OrderIndex = q.OrderIndex })
                .ToList(),
        };
        context.Papers.Add(paper);

        // Updating the Subject row is what makes its concurrency token fire. Marked modified
        // explicitly: if the new value equalled the stored one, EF would skip the UPDATE and
        // with it the check.
        subject.LastGeneratedAt = now;
        context.Entry(subject).Property(s => s.LastGeneratedAt).IsModified = true;

        // One SaveChanges is one transaction: the paper, its lines and the Subject UPDATE
        // commit together, or (on a conflict) not at all.
        await context.SaveChangesAsync(cancellationToken);

        var textById = questions.ToDictionary(q => q.Id, q => q.Text);
        return new GenerationResult.Success(new GeneratedPaper(
            paper.Id,
            subject.Id,
            paper.Title,
            paper.TotalMarks,
            seed,
            solved.AchievedMix!,
            now,
            solved.OrderedQuestions
                .Select(q => new GeneratedQuestion(
                    q.OrderIndex, q.QuestionId, q.ChapterId, q.ChapterName, textById[q.QuestionId], q.Marks, q.Difficulty))
                .ToList()));
    }

    // Question Ids used in the subject's last N papers, newest first by (CreatedAt, Id).
    private static async Task<HashSet<int>> LoadHistoryAsync(
        ExamForgeDbContext context,
        int subjectId,
        int lastN,
        CancellationToken cancellationToken)
    {
        if (lastN == 0)
        {
            return [];
        }

        var recentPaperIds = context.Papers.AsNoTracking()
            .Where(p => p.SubjectId == subjectId)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Take(lastN)
            .Select(p => p.Id);

        var questionIds = await context.PaperQuestions.AsNoTracking()
            .Where(pq => recentPaperIds.Contains(pq.PaperId))
            .Select(pq => pq.QuestionId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

        return questionIds.ToHashSet();
    }

    // The API validates these first; reaching here with a broken spec is a caller bug.
    private static void EnsureConsistent(PaperSpecification specification)
    {
        var distribution = specification.ChapterDistribution;
        if (distribution.Count == 0
            || distribution.Sum(c => c.Marks) != specification.TotalMarks
            || distribution.Select(c => c.ChapterId).Distinct().Count() != distribution.Count
            || specification.ExcludeLastNPapers < 0)
        {
            throw new ArgumentException("The specification is not valid; validate before generating.", nameof(specification));
        }
    }

    // Postgres stores timestamptz to the microsecond. Truncating first means the value returned
    // equals the value read back later.
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
}
