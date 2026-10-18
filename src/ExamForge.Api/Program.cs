using System.Text.Json.Serialization;
using ExamForge.Api.OpenApi;
using ExamForge.Domain.Catalog;
using ExamForge.Domain.Generation;
using ExamForge.Domain.Papers;
using ExamForge.Infrastructure.Catalog;
using ExamForge.Infrastructure.Generation;
using ExamForge.Infrastructure.Papers;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Enums as names ("Easy"), not numbers, in every request and response. Registered twice on
// purpose: controllers serialise with the MVC JSON options, but the OpenAPI generator describes
// types using the minimal-API JSON options. With only the first, the document would claim
// Difficulty is an integer while the API sends "Easy".
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// The OpenAPI document comes from Microsoft.AspNetCore.OpenApi; Swagger UI (below) only renders it.
builder.Services.AddOpenApi(options => options.AddSchemaTransformer<GeneratePaperRequestExample>());

// The connection string is read when the first context is created, not here, so tests can
// supply their own. It is never committed: use user-secrets or ConnectionStrings__ExamForge.
builder.Services.AddDbContextFactory<ExamForgeDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("ExamForge")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:ExamForge is not set. Use `dotnet user-secrets set ConnectionStrings:ExamForge ...` "
            + "in src/ExamForge.Api, or the environment variable ConnectionStrings__ExamForge.")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISeedSource, SystemSeedSource>();
builder.Services.AddScoped<IPaperGenerationService, PaperGenerationService>();
builder.Services.AddScoped<IPaperReadService, PaperReadService>();
builder.Services.AddScoped<ISubjectService, SubjectService>();
builder.Services.AddScoped<IChapterService, ChapterService>();
builder.Services.AddScoped<IQuestionService, QuestionService>();

var app = builder.Build();

// The document and the UI are for local exploration only.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "ExamForge v1"));
}

// No UseHttpsRedirection: the app listens on plain HTTP locally, so the middleware had no HTTPS
// port to redirect to and only logged a warning. In a deployment, TLS ends at the reverse proxy.

app.UseAuthorization();

app.MapControllers();

app.Run();
