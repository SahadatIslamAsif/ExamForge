using ExamForge.Domain.Catalog;
using Npgsql;

namespace ExamForge.Infrastructure.Persistence;

/// <summary>
/// The one place that turns database constraint violations into 409 results. Services check the
/// common cases first (is the question used?), but a concurrent request can slip in between the
/// check and the write; the database constraint still refuses, and this maps that refusal.
/// It lives here, next to the schema, because it is keyed on our constraint names and Postgres
/// error codes: knowledge the API layer should not have.
/// </summary>
public static class DatabaseConflicts
{
    private const string RestrictViolation = "23001";
    private const string ForeignKeyViolation = "23503";
    private const string UniqueViolation = "23505";

    private static readonly string QuestionInUseMessage =
        "This question has been used in a paper, so it cannot be deleted. Deactivate it instead (PATCH with \"isActive\": false).";

    // Constraint name -> the error codes it can raise, and the conflict to report. Matching on the
    // constraint as well as the code matters: a 23503 from another foreign key (say a chapter whose
    // subject is missing) is not "question in use", and must not be reported as one.
    private static readonly Dictionary<string, (string[] SqlStates, string Error, string Message)> Known = new()
    {
        // RESTRICT raises 23001; 23503 is what NO ACTION would raise, kept so a change to the
        // foreign key's delete behaviour cannot turn this into a 500.
        ["FK_PaperQuestions_Questions_QuestionId"] =
            ([RestrictViolation, ForeignKeyViolation], CatalogErrors.QuestionInUse, QuestionInUseMessage),
        ["IX_Chapters_SubjectId_OrderIndex"] =
            ([UniqueViolation], CatalogErrors.ChapterOrderTaken, "Another chapter of this subject already uses this orderIndex."),
    };

    /// <summary>
    /// Runs a write. A known constraint violation becomes a Conflict result; anything else is
    /// rethrown unchanged, because an unexpected violation is a bug and must surface as one.
    /// </summary>
    public static async Task<CatalogResult<T>> GuardAsync<T>(Func<Task<CatalogResult<T>>> write)
    {
        try
        {
            return await write();
        }
        catch (Exception exception) when (Classify(exception) is { } conflict)
        {
            return new CatalogResult<T>.Conflict(conflict.Error, conflict.Message);
        }
    }

    private static (string Error, string Message)? Classify(Exception exception)
    {
        // SaveChanges wraps the PostgresException in a DbUpdateException; ExecuteDelete and raw
        // SQL throw it directly. Walk the chain to find it either way.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.ConstraintName is { } constraint
                && Known.TryGetValue(constraint, out var known)
                && known.SqlStates.Contains(postgres.SqlState))
            {
                return (known.Error, known.Message);
            }
        }

        return null;
    }
}
