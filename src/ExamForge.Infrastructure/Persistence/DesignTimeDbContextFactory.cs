using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ExamForge.Infrastructure.Persistence;

// Used only by `dotnet ef` to build the context at design time. The app itself will get
// its context from DI. Nothing here is committed with a secret: the connection string
// comes from EXAMFORGE_CONNECTION, or from the untracked .env that Docker Compose uses.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ExamForgeDbContext>
{
    public ExamForgeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("EXAMFORGE_CONNECTION")
            ?? FromDotEnv()
            ?? throw new InvalidOperationException(
                "Set EXAMFORGE_CONNECTION, or create .env from .env.example in the repo root.");

        var options = new DbContextOptionsBuilder<ExamForgeDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ExamForgeDbContext(options);
    }

    private static string? FromDotEnv()
    {
        // dotnet ef runs from the project folder, so walk up to the repo root.
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path))
            {
                continue;
            }

            var values = File.ReadLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

            return $"Host=localhost;Port={values["POSTGRES_PORT"]};Database={values["POSTGRES_DB"]};"
                + $"Username={values["POSTGRES_USER"]};Password={values["POSTGRES_PASSWORD"]}";
        }

        return null;
    }
}
