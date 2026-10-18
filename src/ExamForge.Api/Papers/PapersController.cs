using System.Diagnostics;
using ExamForge.Domain.Generation;
using ExamForge.Domain.Papers;
using Microsoft.AspNetCore.Mvc;

namespace ExamForge.Api.Papers;

// Thin by design: validate, call a service, map the result. No DbContext, no generation logic.
[ApiController]
[Route("api/papers")]
public sealed class PapersController(IPaperGenerationService generator, IPaperReadService reader) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<IActionResult> Generate(GeneratePaperRequest request, CancellationToken cancellationToken)
    {
        var errors = GeneratePaperRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            foreach (var (field, messages) in errors)
            {
                foreach (var message in messages)
                {
                    ModelState.AddModelError(field, message);
                }
            }

            return ValidationProblem(ModelState);
        }

        var specification = GeneratePaperRequestValidator.ToSpecification(request);
        var result = await generator.GenerateAsync(specification, cancellationToken);

        return result switch
        {
            GenerationResult.Success success =>
                CreatedAtAction(nameof(GetById), new { id = success.Paper.Id }, PaperMapping.ToResponse(success.Paper)),

            GenerationResult.Infeasible infeasible =>
                UnprocessableEntity(PaperMapping.ToResponse(infeasible, specification.ExcludeLastNPapers)),

            GenerationResult.SubjectNotFound => NotFound(),

            GenerationResult.ChaptersNotInSubject notInSubject => ChaptersNotInSubject(notInSubject, specification.SubjectId),

            GenerationResult.Conflict =>
                Conflict(new ErrorResponse(PaperMapping.ConflictError, PaperMapping.ConflictMessage)),

            _ => throw new UnreachableException($"Unhandled generation result {result.GetType().Name}."),
        };
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var paper = await reader.GetAsync(id, cancellationToken);
        return paper is null ? NotFound() : Ok(PaperMapping.ToResponse(paper));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? subjectId, CancellationToken cancellationToken)
    {
        if (subjectId is null)
        {
            ModelState.AddModelError("subjectId", "subjectId is required.");
            return ValidationProblem(ModelState);
        }

        var papers = await reader.ListForSubjectAsync(subjectId.Value, cancellationToken);
        return papers is null ? NotFound() : Ok(papers.Select(PaperMapping.ToResponse).ToList());
    }

    private IActionResult ChaptersNotInSubject(GenerationResult.ChaptersNotInSubject result, int subjectId)
    {
        ModelState.AddModelError(
            "chapterDistribution",
            $"Chapters {string.Join(", ", result.ChapterIds)} do not exist or do not belong to subject {subjectId}.");
        return ValidationProblem(ModelState);
    }
}
