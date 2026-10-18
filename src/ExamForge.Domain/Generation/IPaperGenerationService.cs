namespace ExamForge.Domain.Generation;

/// <summary>
/// Loads the bank and history, runs the solver and saves the paper. The implementation lives
/// in Infrastructure because it needs the database; the solver itself stays pure.
/// </summary>
public interface IPaperGenerationService
{
    Task<GenerationResult> GenerateAsync(PaperSpecification specification, CancellationToken cancellationToken = default);
}
