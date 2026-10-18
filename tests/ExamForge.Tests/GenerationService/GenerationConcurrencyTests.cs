using ExamForge.Domain.Generation;
using ExamForge.Infrastructure.Generation;
using ExamForge.Infrastructure.Persistence;
using ExamForge.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamForge.Tests.GenerationService;

[Collection(PostgresCollection.Name)]
public class GenerationConcurrencyTests(PostgresFixture postgres) : GenerationTestBase(postgres)
{
    // Alternating 1- and 2-mark questions, 30 marks: room for two disjoint 10-mark papers.
    private static readonly int[] RaceChapter = Enumerable.Range(0, 20).Select(i => i % 2 + 1).ToArray();

    [Fact]
    public async Task Two_parallel_generations_for_one_subject_both_succeed_without_sharing_a_question()
    {
        for (var round = 1; round <= 20; round++)
        {
            var bank = await SeedBankAsync(RaceChapter);

            // Both requests use the same seed and see the same history, so without the
            // concurrency token they would pick identical questions.
            var spec = Spec(bank, [10], seed: 42, excludeLastNPapers: 1);
            var (saves, gate) = SaveGate(participants: 2);
            var service = NewService(onSaving: gate);

            var results = await Task.WhenAll(
                Task.Run(() => service.GenerateAsync(spec)),
                Task.Run(() => service.GenerateAsync(spec)));

            var first = AssertSuccess(results[0]);
            var second = AssertSuccess(results[1]);
            Assert.Empty(Ids(first).Intersect(Ids(second)));
            Assert.Equal(2, await PaperCountAsync(bank.SubjectId));

            // Two first attempts plus exactly one retry: the race really happened.
            Assert.Equal(3, saves());
        }
    }

    [Fact]
    public async Task Parallel_generations_for_different_subjects_do_not_conflict()
    {
        var a = await SeedBankAsync(RaceChapter);
        var b = await SeedBankAsync(RaceChapter);
        var (saves, gate) = SaveGate(participants: 2);
        var service = NewService(onSaving: gate);

        var results = await Task.WhenAll(
            Task.Run(() => service.GenerateAsync(Spec(a, [10], excludeLastNPapers: 1))),
            Task.Run(() => service.GenerateAsync(Spec(b, [10], excludeLastNPapers: 1))));

        Assert.All(results, r => AssertSuccess(r));
        Assert.Equal(2, saves()); // no retry on either side
    }

    [Fact]
    public async Task A_request_that_conflicts_on_every_attempt_returns_conflict_after_three_attempts_and_saves_nothing()
    {
        var bank = await SeedBankAsync(RaceChapter);
        var attempts = 0;

        var service = NewService(onSaving: _ =>
        {
            Interlocked.Increment(ref attempts);
            TouchSubject(bank.SubjectId);
        });

        var result = await service.GenerateAsync(Spec(bank, [10], excludeLastNPapers: 1));

        Assert.Equal(PaperGenerationService.MaxAttempts, Assert.IsType<GenerationResult.Conflict>(result).Attempts);
        Assert.Equal(3, attempts);
        Assert.Equal(0, await PaperCountAsync(bank.SubjectId));

        await using var context = NewContext();
        Assert.Null((await context.Subjects.AsNoTracking().SingleAsync(s => s.Id == bank.SubjectId)).LastGeneratedAt);
    }

    [Fact]
    public async Task A_retry_after_a_conflict_reuses_the_seed_it_started_with()
    {
        var bank = await SeedBankAsync(RaceChapter);
        var seeds = new SequenceSeedSource(1000);
        var attempts = 0;

        // Interfere with the first attempt only.
        var service = NewService(seeds, onSaving: _ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                TouchSubject(bank.SubjectId);
            }
        });

        var paper = AssertSuccess(await service.GenerateAsync(Spec(bank, [10], seed: null)));

        Assert.Equal(2, attempts);
        Assert.Equal(1, seeds.Calls);
        Assert.Equal(1000, paper.Seed);
        await using var context = NewContext();
        Assert.Equal(1000, (await context.Papers.AsNoTracking().SingleAsync(p => p.Id == paper.Id)).Seed);
    }

    // Changes the Subject row from a separate connection, as another request's commit would.
    // Any UPDATE gives the row a new xmin, so the service's pending UPDATE no longer matches.
    private void TouchSubject(int subjectId)
    {
        using var connection = new NpgsqlConnection(Database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand("""UPDATE "Subjects" SET "Name" = "Name" WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", subjectId);
        command.ExecuteNonQuery();
    }

    // Holds the first <participants> SaveChanges calls until all of them have arrived, so both
    // requests have loaded their data before either commits. Later calls (retries) pass
    // straight through. Returns a counter of all SaveChanges calls.
    private static (Func<int> Saves, Action<ExamForgeDbContext> Gate) SaveGate(int participants)
    {
        var barrier = new Barrier(participants);
        var calls = 0;

        void Gate(ExamForgeDbContext _)
        {
            if (Interlocked.Increment(ref calls) <= participants)
            {
                // Timeout so a broken test fails instead of hanging.
                barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            }
        }

        return (() => Volatile.Read(ref calls), Gate);
    }
}
