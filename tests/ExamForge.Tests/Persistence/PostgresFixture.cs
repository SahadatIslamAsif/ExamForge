using System.Collections.Concurrent;
using ExamForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ExamForge.Tests.Persistence;

// One Postgres container for the whole test run. xUnit builds this once for the collection
// (see PostgresCollection), calls InitializeAsync before the first test class that uses
// it, and DisposeAsync after the last.
public sealed class PostgresFixture : IAsyncLifetime
{
    // Same major version as docker-compose.yml, so the tests run against what we ship.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    private readonly ConcurrentDictionary<Type, Lazy<Task<TestDatabase>>> _databases = new();

    public async Task InitializeAsync()
    {
        try
        {
            // First run pulls the image, which can take a while; after that it is quick.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await _container.StartAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            // Fail every test with a readable reason instead of a Docker stack trace.
            throw new InvalidOperationException(
                "Could not start the Postgres test container. Is Docker Desktop running? "
                + "These tests need Docker (they start their own postgres:18, not the compose database).",
                ex);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    // Each test class asks for its database by its own type. The first request creates and
    // migrates it; later requests (one per test method) reuse it.
    public Task<TestDatabase> GetDatabaseAsync(Type testClass) =>
        _databases.GetOrAdd(testClass, _ => new Lazy<Task<TestDatabase>>(CreateDatabaseAsync)).Value;

    private async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
        }.ConnectionString;

        var database = new TestDatabase(connectionString);

        // The real migrations, not EnsureCreated: this is what proves the migration itself.
        await using var context = database.NewContext();
        await context.Database.MigrateAsync();

        return database;
    }
}

public sealed class TestDatabase(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    public ExamForgeDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ExamForgeDbContext>().UseNpgsql(ConnectionString).Options);
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
