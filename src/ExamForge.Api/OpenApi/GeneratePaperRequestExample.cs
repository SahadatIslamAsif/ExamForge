using System.Text.Json.Nodes;
using ExamForge.Api.Papers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ExamForge.Api.OpenApi;

/// <summary>
/// Puts the request from docs/SPEC.md on the GeneratePaperRequest schema, so Swagger UI's
/// "Try it out" starts from a realistic body instead of zeros and "string". The chapter ids
/// exist in a freshly seeded bank (chapters 1–6), so the example works as sent.
/// </summary>
public sealed class GeneratePaperRequestExample : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(GeneratePaperRequest))
        {
            // OpenAPI 3.1 (what .NET 10 emits) puts examples on a schema as an array.
            schema.Examples = [new JsonObject
            {
                ["subjectId"] = 1,
                ["title"] = "Physics Practice — Week 6",
                ["totalMarks"] = 50,
                ["chapterDistribution"] = new JsonArray(
                    new JsonObject { ["chapterId"] = 3, ["marks"] = 20 },
                    new JsonObject { ["chapterId"] = 4, ["marks"] = 20 },
                    new JsonObject { ["chapterId"] = 5, ["marks"] = 10 }),
                ["difficultyMix"] = new JsonObject { ["easy"] = 0.3, ["medium"] = 0.5, ["hard"] = 0.2 },
                ["excludeLastNPapers"] = 3,
                ["seed"] = 20261018,
            }];
        }

        return Task.CompletedTask;
    }
}
