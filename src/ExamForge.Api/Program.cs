using System.Text.Json.Serialization;
using ExamForge.Domain.Catalog;
using ExamForge.Domain.Generation;
using ExamForge.Domain.Papers;
using ExamForge.Infrastructure.Catalog;
using ExamForge.Infrastructure.Generation;
using ExamForge.Infrastructure.Papers;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Enums as names ("Easy"), not numbers, in every request and response.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
