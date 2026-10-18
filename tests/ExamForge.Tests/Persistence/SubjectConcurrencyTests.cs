using Microsoft.EntityFrameworkCore;

namespace ExamForge.Tests.Persistence;

// Subject.Version is Postgres's xmin. EF adds "WHERE xmin = <value it loaded>" to the
// UPDATE; if another transaction changed the row meanwhile, zero rows match and EF throws.
[Collection(PostgresCollection.Name)]
public class SubjectConcurrencyTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    [Fact]
    public async Task Second_writer_of_the_same_subject_gets_a_concurrency_exception()
    {
        var subjectId = await SeedSubjectAsync();

        // Two separate contexts model two overlapping requests: both read the same version.
        await using var first = NewContext();
        await using var second = NewContext();
        var firstCopy = await first.Subjects.SingleAsync(s => s.Id == subjectId);
        var secondCopy = await second.Subjects.SingleAsync(s => s.Id == subjectId);

        firstCopy.LastGeneratedAt = DateTimeOffset.UtcNow;
        secondCopy.LastGeneratedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Different_subjects_updated_in_parallel_do_not_conflict()
    {
        var subjectA = await SeedSubjectAsync();
        var subjectB = await SeedSubjectAsync();

        await Task.WhenAll(Touch(subjectA), Touch(subjectB));

        async Task Touch(int id)
        {
            await using var context = NewContext();
            var subject = await context.Subjects.SingleAsync(s => s.Id == id);
            subject.LastGeneratedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync();
        }
    }
}
