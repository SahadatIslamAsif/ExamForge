using ExamForge.Domain.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace ExamForge.Api.Catalog;

[ApiController]
[Route("api/subjects")]
public sealed class SubjectsController(ISubjectService subjects) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<SubjectResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateSubjectRequest request, CancellationToken cancellationToken)
    {
        var (errors, subject) = CatalogValidators.Validate(request);
        if (subject is null)
        {
            return this.FieldErrors(errors);
        }

        var created = await subjects.CreateAsync(subject, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToResponse(created));
    }

    [HttpGet]
    [ProducesResponseType<List<SubjectResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok((await subjects.ListAsync(cancellationToken)).Select(ToResponse).ToList());

    [HttpGet("{id:int}")]
    [ProducesResponseType<SubjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken) =>
        await subjects.GetAsync(id, cancellationToken) is { } subject ? Ok(ToResponse(subject)) : NotFound();

    private static SubjectResponse ToResponse(SubjectInfo subject) => new(subject.Id, subject.Name, subject.Level);
}
