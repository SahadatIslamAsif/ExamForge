using Microsoft.Extensions.DependencyInjection;

namespace ExamForge.Infrastructure.Seeding;

/// <summary>
/// `dotnet run --project src/ExamForge.Api -- --seed`: migrate, seed, print one line, exit.
/// Returns the process exit code: 0 when the bank was seeded or seeding was skipped, 1 on any error.
/// </summary>
public static class SeedCommand
{
    public const string Argument = "--seed";

    public static async Task<int> RunAsync(
        IServiceProvider services,
        TextWriter output,
        TextWriter error,
        string? bankPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var path = bankPath
                ?? QuestionBankFile.Find(Directory.GetCurrentDirectory())
                ?? throw new FileNotFoundException(
                    $"{QuestionBankFile.RelativePath} was not found in {Directory.GetCurrentDirectory()} or any parent folder.");

            var bank = QuestionBankFile.Load(path);

            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<QuestionBankSeeder>().SeedAsync(bank, cancellationToken);

            await output.WriteLineAsync(result.Summary);
            return 0;
        }
        catch (Exception exception)
        {
            // The process boundary: report the failure and exit non-zero. The transaction has
            // already rolled back, so nothing half-seeded is left behind.
            await error.WriteLineAsync($"Seeding failed: {exception.Message}");
            return 1;
        }
    }
}
