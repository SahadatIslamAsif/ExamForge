using ExamForge.Domain.Catalog;
using ExamForge.Domain.Entities;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Tests.Persistence;

// The one place that turns constraint violations into 409 results. These tests run the real
// failing statements, without any service pre-check in front, to cover the race where a paper
// starts using a question between the usage check and the delete.
[Collection(PostgresCollection.Name)]
public class DatabaseConflictsTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    [Fact]
    public async Task Deleting_a_used_question_without_a_pre_check_becomes_question_in_use()
    {
        var subjectId = await SeedSubjectAsync();
        var questionId = await SeedQuestionAsync(await SeedChapterAsync(subjectId));
        await AddToPaperAsync(await SeedPaperAsync(subjectId), questionId, orderIndex: 1);

        await using var context = NewContext();
        var result = await DatabaseConflicts.GuardAsync<int>(async () =>
        {
            await context.Questions.Where(q => q.Id == questionId).ExecuteDeleteAsync();
            return new CatalogResult<int>.Ok(questionId);
        });

        var conflict = Assert.IsType<CatalogResult<int>.Conflict>(result);
        Assert.Equal(CatalogErrors.QuestionInUse, conflict.Error);
        Assert.Contains("isActive", conflict.Message);
        Assert.True(await NewContext().Questions.AnyAsync(q => q.Id == questionId));
    }

    [Fact]
    public async Task A_duplicate_chapter_order_becomes_chapter_order_taken()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedChapterAsync(subjectId, orderIndex: 1);

        await using var context = NewContext();
        var result = await DatabaseConflicts.GuardAsync<int>(async () =>
        {
            context.Chapters.Add(new Chapter { SubjectId = subjectId, Name = "Again", OrderIndex = 1 });
            await context.SaveChangesAsync();
            return new CatalogResult<int>.Ok(0);
        });

        Assert.Equal(CatalogErrors.ChapterOrderTaken, Assert.IsType<CatalogResult<int>.Conflict>(result).Error);
    }

    [Fact]
    public async Task Other_constraint_violations_are_not_swallowed()
    {
        var chapterId = await SeedChapterAsync(await SeedSubjectAsync());

        // A check violation (Marks > 0) is a bug in our validation, not a conflict: it must
        // surface as an exception (a 500), never as a misleading 409.
        await using var context = NewContext();
        await Assert.ThrowsAsync<DbUpdateException>(() => DatabaseConflicts.GuardAsync<int>(async () =>
        {
            context.Questions.Add(NewQuestion(chapterId, marks: 0));
            await context.SaveChangesAsync();
            return new CatalogResult<int>.Ok(0);
        }));
    }
}
