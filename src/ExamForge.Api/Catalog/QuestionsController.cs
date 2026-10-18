using ExamForge.Domain.Catalog;
using ExamForge.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ExamForge.Api.Catalog;

[ApiController]
[Route("api/questions")]
public sealed class QuestionsController(IQuestionService questions) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateQuestionRequest request, CancellationToken cancellationToken)
    {
        var (errors, question) = CatalogValidators.Validate(request);
        if (question is null)
        {
            return this.FieldErrors(errors);
        }

        var result = await questions.CreateAsync(question, cancellationToken);
        return this.FromCatalog(result, created => CreatedAtAction(nameof(GetById), new { id = created.Id }, ToResponse(created)));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? chapterId, [FromQuery] string? difficulty, CancellationToken cancellationToken)
    {
        Difficulty? parsed = null;
        if (difficulty is not null)
        {
            var errors = new Dictionary<string, List<string>>();
            parsed = CatalogValidators.RequireEnum<Difficulty>(errors, "difficulty", difficulty);
            if (errors.Count > 0)
            {
                return this.FieldErrors(errors);
            }
        }

        var listed = await questions.ListAsync(chapterId, parsed, cancellationToken);
        return Ok(listed.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken) =>
        await questions.GetAsync(id, cancellationToken) is { } question ? Ok(ToResponse(question)) : NotFound();

    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateQuestionRequest request, CancellationToken cancellationToken)
    {
        var (errors, changes) = CatalogValidators.Validate(request);
        if (changes is null)
        {
            return this.FieldErrors(errors);
        }

        var result = await questions.UpdateAsync(id, changes, cancellationToken);
        return this.FromCatalog(result, updated => Ok(ToResponse(updated)));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        this.FromCatalog(await questions.DeleteAsync(id, cancellationToken), _ => NoContent());

    private static QuestionResponse ToResponse(QuestionInfo q) =>
        new(q.Id, q.ChapterId, q.Text, q.Marks, q.Difficulty, q.Type, q.IsActive, q.CreatedAt.UtcDateTime, q.UsedInPapers);
}
