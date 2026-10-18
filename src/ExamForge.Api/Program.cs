using System.Text.Json.Serialization;
using ExamForge.Api.OpenApi;
using ExamForge.Domain.Catalog;
using ExamForge.Domain.Generation;
using ExamForge.Domain.Papers;
using ExamForge.Infrastructure.Catalog;
using ExamForge.Infrastructure.Generation;
using ExamForge.Infrastructure.Papers;
using ExamForge.Infrastructure.Persistence;
using ExamForge.Infrastructure.Seeding;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// `-- --seed` loads seed/questions.json and exits instead of serving. EF's logs are quietened for
// that run so the output is the command's one-line summary. The command log is silenced fully:
// on a new database EF probes the not-yet-created migrations table and logs that as an error,
// although it is expected. Real failures still surface: SeedCommand prints them and exits 1.
var seeding = args.Contains(SeedCommand.Argument);
if (seeding)
{
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
}

// Enums as names ("Easy"), not numbers, in every request and response. Registered twice on
// purpose: controllers serialise with the MVC JSON options, but the OpenAPI generator describes
// types using the minimal-API JSON options. With only the first, the document would claim
// Difficulty is an integer while the API sends "Easy".
builder.Services.AddControllers(options =>
    {
        // JSON only: drop the text/plain formatter and the legacy text/json media type, so content
        // negotiation and the OpenAPI document offer application/json alone. application/*+json
        // stays on the output side because errors are written as application/problem+json.
        options.OutputFormatters.RemoveType<StringOutputFormatter>();
        options.OutputFormatters.OfType<SystemTextJsonOutputFormatter>().Single().SupportedMediaTypes.Remove("text/json");
        var input = options.InputFormatters.OfType<SystemTextJsonInputFormatter>().Single();
        input.SupportedMediaTypes.Remove("text/json");
        input.SupportedMediaTypes.Remove("application/*+json");
    })
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
builder.Services.AddScoped<QuestionBankSeeder>();

var app = builder.Build();

if (seeding)
{
    return await SeedCommand.RunAsync(app.Services, Console.Out, Console.Error);
}

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
return 0;
