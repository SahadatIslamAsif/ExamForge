using ExamForge.Domain.Generation;
using ExamForge.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Tests.GenerationService;

// History papers are written directly with fixed 2025 dates, so which papers are "the last N"
// is exact and does not depend on the clock.
[Collection(PostgresCollection.Name)]
public class GenerationHistoryTests(PostgresFixture postgres) : GenerationTestBase(postgres)
{
    [Fact]
    public async Task Questions_used_in_the_last_three_papers_are_excluded_with_n_3()
    {
        // Ten 2-mark questions; five of them are spread over the last three papers. The only
        // way to make 10 marks without them is the other five, all of them.
        var bank = await SeedBankAsync(Enumerable.Repeat(2, 10).ToArray());
        var q = bank.QuestionIds[0];
        await SeedHistoryPaperAsync(bank.SubjectId, Day(1), q[0], q[1]);
        await SeedHistoryPaperAsync(bank.SubjectId, Day(2), q[2], q[3]);
        await SeedHistoryPaperAsync(bank.SubjectId, Day(3), q[4]);

        var paper = AssertSuccess(await NewService().GenerateAsync(Spec(bank, [10], excludeLastNPapers: 3)));

        Assert.Equal(q.Skip(5).Order(), Ids(paper).Order());
    }

    [Fact]
    public async Task A_question_that_has_aged_out_of_the_window_is_eligible_again()
    {
        // Three 5-mark questions, each used in one paper; X is in the oldest.
        var bank = await SeedBankAsync([5, 5, 5]);
        var (x, y, z) = (bank.QuestionIds[0][0], bank.QuestionIds[0][1], bank.QuestionIds[0][2]);
        await SeedHistoryPaperAsync(bank.SubjectId, Day(1), x);
        await SeedHistoryPaperAsync(bank.SubjectId, Day(2), y);
        await SeedHistoryPaperAsync(bank.SubjectId, Day(3), z);

        // N = 3 covers all three papers: nothing is left.
        var excluded = Assert.Single(AssertInfeasible(await NewService().GenerateAsync(Spec(bank, [5], excludeLastNPapers: 3))));
        Assert.Equal(15, excluded.ExcludedMarks);

        // N = 2 covers only the two newest: X has aged out and is the only choice.
        var paper = AssertSuccess(await NewService().GenerateAsync(Spec(bank, [5], excludeLastNPapers: 2)));
        Assert.Equal([x], Ids(paper));
    }

    [Fact]
    public async Task Zero_disables_the_history_window()
    {
        var bank = await SeedBankAsync([5]);
        await SeedHistoryPaperAsync(bank.SubjectId, Day(1), bank.QuestionIds[0][0]);

        var paper = AssertSuccess(await NewService().GenerateAsync(Spec(bank, [5], excludeLastNPapers: 0)));

        Assert.Equal([bank.QuestionIds[0][0]], Ids(paper));
    }

    [Fact]
    public async Task A_window_that_empties_the_pool_is_infeasible_with_the_excluded_marks_reported()
    {
        var bank = await SeedBankAsync([3, 2]);
        await SeedHistoryPaperAsync(bank.SubjectId, Day(1), bank.QuestionIds[0][0], bank.QuestionIds[0][1]);

        var failure = Assert.Single(AssertInfeasible(await NewService().GenerateAsync(Spec(bank, [5], excludeLastNPapers: 1))));

        Assert.Equal(FailureConstraint.ChapterDistribution, failure.Failure.Constraint);
        Assert.Equal(0, failure.Failure.AvailableMarks);
        Assert.Equal(5, failure.ExcludedMarks);
        Assert.Equal(1, await PaperCountAsync(bank.SubjectId)); // only the history paper
    }

    [Fact]
    public async Task Papers_of_another_subject_do_not_push_this_subjects_papers_out_of_the_window()
    {
        // Subject A's only paper used X. Subject B then produced three newer papers.
        // "The last 1 paper" for A must still be A's own paper, so X stays excluded.
        var a = await SeedBankAsync([5]);
        var b = await SeedBankAsync([1, 1, 1]);
        await SeedHistoryPaperAsync(a.SubjectId, Day(1), a.QuestionIds[0][0]);
        await SeedHistoryPaperAsync(b.SubjectId, Day(2), b.QuestionIds[0][0]);
        await SeedHistoryPaperAsync(b.SubjectId, Day(3), b.QuestionIds[0][1]);
        await SeedHistoryPaperAsync(b.SubjectId, Day(4), b.QuestionIds[0][2]);

        var failure = Assert.Single(AssertInfeasible(await NewService().GenerateAsync(Spec(a, [5], excludeLastNPapers: 1))));

        Assert.Equal(5, failure.ExcludedMarks);
    }

    [Fact]
    public async Task Inactive_questions_are_never_selected()
    {
        // 2 + 3 active, plus an inactive 3.
        var bank = await SeedBankAsync([2, 3, 3]);
        var inactive = bank.QuestionIds[0][1];
        await using (var context = NewContext())
        {
            await context.Questions.Where(q => q.Id == inactive)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.IsActive, false));
        }

        var paper = AssertSuccess(await NewService().GenerateAsync(Spec(bank, [5])));
        Assert.DoesNotContain(inactive, Ids(paper));

        // 8 marks would need the inactive question too.
        var failure = Assert.Single(AssertInfeasible(await NewService().GenerateAsync(Spec(bank, [8]))));
        Assert.Equal(5, failure.Failure.AvailableMarks);
        Assert.Equal(0, failure.ExcludedMarks);
    }
}
