using ExamForge.Domain.Catalog;
using ExamForge.Domain.Enums;

namespace ExamForge.Api.Catalog;

/// <summary>
/// Shape rules for the catalog endpoints, checked before any database access. Rules that need
/// the database (does this subject or chapter exist?) are reported by the services.
/// Each method returns the errors, and the domain command when there are none.
/// </summary>
public static class CatalogValidators
{
    // Match the column lengths in the EF configuration, so a long value is a 400, not a 500.
    private const int SubjectNameMaxLength = 200;
    private const int LevelMaxLength = 100;
    private const int ChapterNameMaxLength = 200;

    public static (Dictionary<string, List<string>> Errors, NewSubject? Subject) Validate(CreateSubjectRequest request)
    {
        var errors = new Dictionary<string, List<string>>();
        RequireText(errors, "name", request.Name, SubjectNameMaxLength);
        RequireText(errors, "level", request.Level, LevelMaxLength);

        return errors.Count > 0 ? (errors, null) : (errors, new NewSubject(request.Name!.Trim(), request.Level!.Trim()));
    }

    public static (Dictionary<string, List<string>> Errors, NewChapter? Chapter) Validate(CreateChapterRequest request)
    {
        var errors = new Dictionary<string, List<string>>();
        if (request.SubjectId is null)
        {
            errors.AddError("subjectId", "subjectId is required.");
        }

        RequireText(errors, "name", request.Name, ChapterNameMaxLength);

        if (request.OrderIndex is null)
        {
            errors.AddError("orderIndex", "orderIndex is required.");
        }
        else if (request.OrderIndex < 1)
        {
            errors.AddError("orderIndex", "orderIndex must be 1 or greater.");
        }

        return errors.Count > 0
            ? (errors, null)
            : (errors, new NewChapter(request.SubjectId!.Value, request.Name!.Trim(), request.OrderIndex!.Value));
    }

    public static (Dictionary<string, List<string>> Errors, NewQuestion? Question) Validate(CreateQuestionRequest request)
    {
        var errors = new Dictionary<string, List<string>>();
        if (request.ChapterId is null)
        {
            errors.AddError("chapterId", "chapterId is required.");
        }

        RequireText(errors, "text", request.Text, maxLength: null);

        if (request.Marks is null)
        {
            errors.AddError("marks", "marks is required.");
        }
        else if (request.Marks <= 0)
        {
            errors.AddError("marks", "marks must be positive.");
        }

        var difficulty = RequireEnum<Difficulty>(errors, "difficulty", request.Difficulty);
        var type = RequireEnum<QuestionType>(errors, "type", request.Type);

        return errors.Count > 0
            ? (errors, null)
            : (errors, new NewQuestion(
                request.ChapterId!.Value, request.Text!.Trim(), request.Marks!.Value, difficulty!.Value, type!.Value,
                request.IsActive ?? true));
    }

    public static (Dictionary<string, List<string>> Errors, QuestionChanges? Changes) Validate(UpdateQuestionRequest request)
    {
        var errors = new Dictionary<string, List<string>>();

        if (request.Text is not null)
        {
            RequireText(errors, "text", request.Text, maxLength: null);
        }

        if (request.Marks is <= 0)
        {
            errors.AddError("marks", "marks must be positive.");
        }

        var difficulty = request.Difficulty is null ? null : RequireEnum<Difficulty>(errors, "difficulty", request.Difficulty);
        var type = request.Type is null ? null : RequireEnum<QuestionType>(errors, "type", request.Type);

        return errors.Count > 0
            ? (errors, null)
            : (errors, new QuestionChanges(request.ChapterId, request.Text?.Trim(), request.Marks, difficulty, type, request.IsActive));
    }

    /// <summary>Parses an enum by its exact name; numbers ("3") and misspellings are rejected.</summary>
    public static TEnum? RequireEnum<TEnum>(Dictionary<string, List<string>> errors, string field, string? value)
        where TEnum : struct, Enum
    {
        var names = Enum.GetNames<TEnum>();
        if (value is null)
        {
            errors.AddError(field, $"{field} is required: one of {string.Join(", ", names)}.");
            return null;
        }

        if (!names.Contains(value))
        {
            errors.AddError(field, $"'{value}' is not a valid {field}: use one of {string.Join(", ", names)}.");
            return null;
        }

        return Enum.Parse<TEnum>(value);
    }

    private static void RequireText(Dictionary<string, List<string>> errors, string field, string? value, int? maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.AddError(field, $"{field} is required and must not be blank.");
        }
        else if (value.Trim().Length > maxLength)
        {
            errors.AddError(field, $"{field} must be at most {maxLength} characters.");
        }
    }
}
