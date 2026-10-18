using Microsoft.AspNetCore.Mvc;

namespace ExamForge.Api;

/// <summary>
/// Shared by every controller: request bodies must be application/json. Responses are JSON too,
/// but that is set on the formatters in Program.cs rather than with [Produces] here: [Produces]
/// would overwrite the application/problem+json type of every 400 and 404 body.
/// </summary>
[ApiController]
[Consumes("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
}
