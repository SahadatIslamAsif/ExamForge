namespace ExamForge.Tests.Persistence;

// SqlState 23505 is Postgres's unique_violation.
[Collection(PostgresCollection.Name)]
public class UniquenessTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    [Fact]
    public async Task The_same_question_cannot_appear_twice_in_a_paper()
    {
        var subjectId = await SeedSubjectAsync();
        var chapterId = await SeedChapterAsync(subjectId);
        var questionId = await SeedQuestionAsync(chapterId);
        var paperId = await SeedPaperAsync(subjectId);
        await AddToPaperAsync(paperId, questionId, orderIndex: 1);

        await AssertPostgresErrorAsync("23505", () => AddToPaperAsync(paperId, questionId, orderIndex: 2));
    }

    [Fact]
    public async Task Two_questions_cannot_share_a_position_in_a_paper()
    {
        var subjectId = await SeedSubjectAsync();
        var chapterId = await SeedChapterAsync(subjectId);
        var first = await SeedQuestionAsync(chapterId);
        var second = await SeedQuestionAsync(chapterId);
        var paperId = await SeedPaperAsync(subjectId);
        await AddToPaperAsync(paperId, first, orderIndex: 1);

        await AssertPostgresErrorAsync("23505", () => AddToPaperAsync(paperId, second, orderIndex: 1));
    }

    [Fact]
    public async Task Two_chapters_in_a_subject_cannot_share_an_order_index()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedChapterAsync(subjectId, orderIndex: 1);

        await AssertPostgresErrorAsync("23505", () => SeedChapterAsync(subjectId, orderIndex: 1));
    }

    [Fact]
    public async Task Different_subjects_can_use_the_same_chapter_order_index()
    {
        await SeedChapterAsync(await SeedSubjectAsync(), orderIndex: 1);

        // The uniqueness is per subject, so this must not throw.
        await SeedChapterAsync(await SeedSubjectAsync(), orderIndex: 1);
    }
}
