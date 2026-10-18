using ExamForge.Domain.Enums;

namespace ExamForge.Api.Papers;

// CreatedAt is a UTC DateTime rather than a DateTimeOffset so it serialises as "...Z", the
// form in the spec; a DateTimeOffset would serialise as "...+00:00".
public sealed record PaperResponse(
    int Id,
    int SubjectId,
    string Title,
    int TotalMarks,
    long Seed,
    DifficultyMixResponse AchievedDifficultyMix,
    IReadOnlyList<PaperQuestionResponse> Questions,
    DateTime CreatedAt);

public sealed record PaperQuestionResponse(
    int OrderIndex,
    int QuestionId,
    int ChapterId,
    string ChapterName,
    string Text,
    int Marks,
    Difficulty Difficulty);

public sealed record DifficultyMixResponse(double Easy, double Medium, double Hard);

public sealed record PaperSummaryResponse(int Id, string Title, int TotalMarks, long Seed, DateTime CreatedAt);

public sealed record InfeasibleResponse(string Error, string Message, IReadOnlyList<FailureResponse> Failures);

public sealed record FailureResponse(
    string Constraint,
    int ChapterId,
    string ChapterName,
    int RequestedMarks,
    int AvailableMarks,
    string Detail);

public sealed record ErrorResponse(string Error, string Message);
