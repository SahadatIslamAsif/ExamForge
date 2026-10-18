using ExamForge.Domain.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace ExamForge.Api.Catalog;

[ApiController]
[Route("api/chapters")]
public sealed class ChaptersController(IChapterService chapters) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateChapterRequest request, CancellationToken cancellationToken)
    {
        var (errors, chapter) = CatalogValidators.Validate(request);
        if (chapter is null)
        {
            return this.FieldErrors(errors);
        }

        var result = await chapters.CreateAsync(chapter, cancellationToken);
        return this.FromCatalog(result, created => CreatedAtAction(nameof(GetById), new { id = created.Id }, ToResponse(created)));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? subjectId, CancellationToken cancellationToken)
    {
        if (subjectId is null)
        {
            return this.FieldError("subjectId", "subjectId is required.");
        }

        var listed = await chapters.ListForSubjectAsync(subjectId.Value, cancellationToken);
        return listed is null ? NotFound() : Ok(listed.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken) =>
        await chapters.GetAsync(id, cancellationToken) is { } chapter ? Ok(ToResponse(chapter)) : NotFound();

    private static ChapterResponse ToResponse(ChapterInfo chapter) =>
        new(chapter.Id, chapter.SubjectId, chapter.Name, chapter.OrderIndex);
}
