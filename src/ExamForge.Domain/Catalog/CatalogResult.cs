namespace ExamForge.Domain.Catalog;

/// <summary>
/// Outcome of a catalog (subject, chapter, question) operation: exactly one of the nested cases.
/// Services return these instead of throwing, so the API maps them in one place.
/// </summary>
public abstract record CatalogResult<T>
{
    private CatalogResult()
    {
    }

    public sealed record Ok(T Value) : CatalogResult<T>;

    public sealed record NotFound : CatalogResult<T>;

    /// <summary>A rule that needs the database failed, e.g. a referenced chapter does not exist. Maps to 400.</summary>
    public sealed record Invalid(string Field, string Message) : CatalogResult<T>;

    /// <summary>The request is well-formed but clashes with stored data. Maps to 409.</summary>
    public sealed record Conflict(string Error, string Message) : CatalogResult<T>;
}

public static class CatalogErrors
{
    public const string QuestionInUse = "QUESTION_IN_USE";
    public const string ChapterOrderTaken = "CHAPTER_ORDER_TAKEN";
}
