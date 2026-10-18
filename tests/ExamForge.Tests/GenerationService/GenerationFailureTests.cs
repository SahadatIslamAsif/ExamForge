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
    public async Task Chapters_outside_the_subject_are_listed_and_nothing_is_saved()
    {
        var bank = await SeedBankAsync([5, 5]);
        var other = await SeedBankAsync([5, 5]);
        const int missingChapterId = 999_999;
        var spec = new PaperSpecification(
            bank.SubjectId,
            "Mixed",
            15,
            [new ChapterMarks(bank.ChapterIds[0], 5), new ChapterMarks(missingChapterId, 5), new ChapterMarks(other.ChapterIds[0], 5)],
            DefaultMix,
            0,
            42);

        var result = await NewService().GenerateAsync(spec);

        var notInSubject = Assert.IsType<GenerationResult.ChaptersNotInSubject>(result);
        Assert.Equal([other.ChapterIds[0], missingChapterId], notInSubject.ChapterIds);
        Assert.Equal(0, await PaperCountAsync(bank.SubjectId));
    }

    [Fact]
    public async Task An_unknown_subject_is_reported_before_its_chapters_are_checked()
    {
        var other = await SeedBankAsync([5]);
        var spec = new PaperSpecification(999_999, "Nope", 5, [new ChapterMarks(other.ChapterIds[0], 5)], DefaultMix, 0, 42);

        Assert.IsType<GenerationResult.SubjectNotFound>(await NewService().GenerateAsync(spec));
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
