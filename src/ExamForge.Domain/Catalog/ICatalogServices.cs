using ExamForge.Domain.Enums;

namespace ExamForge.Domain.Catalog;

public interface ISubjectService
{
    Task<SubjectInfo> CreateAsync(NewSubject subject, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubjectInfo>> ListAsync(CancellationToken cancellationToken = default);

    Task<SubjectInfo?> GetAsync(int id, CancellationToken cancellationToken = default);
}

public interface IChapterService
{
    Task<CatalogResult<ChapterInfo>> CreateAsync(NewChapter chapter, CancellationToken cancellationToken = default);

    /// <summary>The subject's chapters by OrderIndex; null if the subject does not exist.</summary>
    Task<IReadOnlyList<ChapterInfo>?> ListForSubjectAsync(int subjectId, CancellationToken cancellationToken = default);

    Task<ChapterInfo?> GetAsync(int id, CancellationToken cancellationToken = default);
}

public interface IQuestionService
{
    Task<CatalogResult<QuestionInfo>> CreateAsync(NewQuestion question, CancellationToken cancellationToken = default);

    /// <summary>Questions by Id, optionally filtered by chapter and difficulty.</summary>
    Task<IReadOnlyList<QuestionInfo>> ListAsync(int? chapterId, Difficulty? difficulty, CancellationToken cancellationToken = default);

    Task<QuestionInfo?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Once a question is in any paper, only IsActive may change (QUESTION_IN_USE otherwise).</summary>
    Task<CatalogResult<QuestionInfo>> UpdateAsync(int id, QuestionChanges changes, CancellationToken cancellationToken = default);

    /// <summary>Deletes a question that no paper uses; QUESTION_IN_USE otherwise.</summary>
    Task<CatalogResult<int>> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
