using ExamForge.Domain.Generation;
using ExamForge.Infrastructure.Persistence;
using ExamForge.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Tests.GenerationService;

// A clock that moves forward one second on every read, so papers saved one after another
// get distinct, increasing CreatedAt values. Thread-safe for the parallel tests.
public sealed class FakeTimeProvider : TimeProvider
{
    private long _ticks = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero).UtcTicks;

    public override DateTimeOffset GetUtcNow() =>
        new(Interlocked.Add(ref _ticks, TimeSpan.TicksPerSecond), TimeSpan.Zero);
}

// Returns start, start + 1, ... and counts calls, so a test can tell whether a retry asked
// for a second seed.
public sealed class SequenceSeedSource(long start) : ISeedSource
{
    private long _calls;

    public int Calls => (int)Interlocked.Read(ref _calls);

    public long NextSeed() => start + Interlocked.Increment(ref _calls) - 1;
}

// Gives the service a new context per attempt, like the real factory, and lets a test run
// code at the moment SaveChanges starts (before EF opens its transaction).
public sealed class TestDbContextFactory(TestDatabase database, Action<ExamForgeDbContext>? onSaving)
    : IDbContextFactory<ExamForgeDbContext>
{
    public ExamForgeDbContext CreateDbContext()
    {
        var context = database.NewContext();
        if (onSaving is not null)
        {
            context.SavingChanges += (_, _) => onSaving(context);
        }

        return context;
    }
}
