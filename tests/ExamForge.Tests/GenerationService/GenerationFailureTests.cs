using ExamForge.Domain.Generation;
using ExamForge.Tests.Persistence;

namespace ExamForge.Tests.GenerationService;

[Collection(PostgresCollection.Name)]
public class GenerationFailureTests(PostgresFixture postgres) : GenerationTestBase(postgres)
{
    [Fact]
    public async Task One_short_chapter_is_reported_and_nothing_is_saved()
    {
        var bank = await SeedBankAsync([1, 2, 3], [5, 5, 5, 5]);

        var failures = AssertInfeasible(await NewService().GenerateAsync(Spec(bank, [10, 10])));

        var failure = Assert.Single(failures);
        Assert.Equal(FailureConstraint.ChapterDistribution, failure.Failure.Constraint);
        Assert.Equal(bank.ChapterIds[0], failure.Failure.ChapterId);
        Assert.Equal("Chapter 1", failure.Failure.ChapterName);
        Assert.Equal(10, failure.Failure.RequestedMarks);
        Assert.Equal(6, failure.Failure.AvailableMarks);
        Assert.Equal(0, failure.ExcludedMarks);
        Assert.Equal(0, await PaperCountAsync(bank.SubjectId));
    }

    [Fact]
    public async Task Two_short_chapters_are_both_reported()
    {
        var bank = await SeedBankAsync([1, 2], [3], [5, 5, 5]);

        var failures = AssertInfeasible(await NewService().GenerateAsync(Spec(bank, [10, 10, 5])));

        Assert.Equal([bank.ChapterIds[0], bank.ChapterIds[1]], failures.Select(f => f.Failure.ChapterId));
        Assert.All(failures, f => Assert.Equal(FailureConstraint.ChapterDistribution, f.Failure.Constraint));
        Assert.Equal(0, await PaperCountAsync(bank.SubjectId));
    }

    [Fact]
    public async Task Enough_marks_but_no_exact_combination_is_an_exact_marks_failure()
    {
        var bank = await SeedBankAsync([2, 4, 6]);

        var failures = AssertInfeasible(await NewService().GenerateAsync(Spec(bank, [9])));

        var failure = Assert.Single(failures);
        Assert.Equal(FailureConstraint.ExactMarks, failure.Failure.Constraint);
        Assert.Equal(9, failure.Failure.RequestedMarks);
        Assert.Equal(12, failure.Failure.AvailableMarks);
        Assert.Equal(0, await PaperCountAsync(bank.SubjectId));
    }
}
