using ExamForge.Domain.Entities;
using ExamForge.Infrastructure.Persistence;
using ExamForge.Infrastructure.Seeding;
using ExamForge.Tests.GenerationService;
using ExamForge.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamForge.Tests.Seeding;

// Each test gets a brand-new database, because the seeder's behaviour depends on what is already there.
[Collection(PostgresCollection.Name)]
public class QuestionBankSeederTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    private static readonly QuestionBankFile Bank = QuestionBankFile.Load(SeedFileTests.BankPath());

    private static QuestionBankSeeder Seeder(TestDatabase database, Action<ExamForgeDbContext>? onSaving = null) =>
        new(new TestDbContextFactory(database, onSaving), new FakeTimeProvider());

    private static async Task<(int Subjects, int Chapters, int Questions)> CountsAsync(TestDatabase database)
    {
        await using var context = database.NewContext();
        return (await context.Subjects.CountAsync(), await context.Chapters.CountAsync(), await context.Questions.CountAsync());
    }

    [Fact]
    public async Task An_empty_database_is_migrated_and_seeded()
    {
        var database = await Postgres.CreateDatabaseAsync(migrate: false);

        var result = await Seeder(database).SeedAsync(Bank);

        Assert.True(result.Seeded);
        Assert.Equal(1, result.MigrationsApplied);
        Assert.Equal((1, 6, 72), (result.Subjects, result.Chapters, result.Questions));
        Assert.Equal("Applied 1 migration. Seeded 1 subject, 6 chapters and 72 questions.", result.Summary);
        Assert.Equal((1, 6, 72), await CountsAsync(database));

        // CreatedAt comes from the injected clock: the fake's first reading.
        await using var context = database.NewContext();
        var createdAt = await context.Questions.Select(q => q.CreatedAt).Distinct().ToListAsync();
        Assert.Equal([new DateTimeOffset(2026, 1, 1, 9, 0, 1, TimeSpan.Zero)], createdAt);
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        var database = await Postgres.CreateDatabaseAsync();
        await Seeder(database).SeedAsync(Bank);

        var second = await Seeder(database).SeedAsync(Bank);

        Assert.False(second.Seeded);
        Assert.Contains("nothing was changed", second.Summary);
        Assert.Equal((1, 6, 72), await CountsAsync(database));
    }

    [Fact]
    public async Task It_refuses_when_a_different_subject_already_exists()
    {
        var database = await Postgres.CreateDatabaseAsync();
        await using (var context = database.NewContext())
        {
            context.Subjects.Add(new Subject { Name = "Chemistry", Level = "O Level" });
            await context.SaveChangesAsync();
        }

        var result = await Seeder(database).SeedAsync(Bank);

        Assert.False(result.Seeded);
        Assert.Equal((1, 0, 0), await CountsAsync(database));
        await using var check = database.NewContext();
        Assert.Equal("Chemistry", (await check.Subjects.SingleAsync()).Name);
    }

    [Fact]
    public async Task A_failure_part_way_through_leaves_nothing_behind()
    {
        var database = await Postgres.CreateDatabaseAsync();
        var saves = 0;

        // The seeder saves the subject, then one chapter at a time. Fail the third save.
        var seeder = Seeder(database, _ =>
        {
            if (++saves == 3)
            {
                throw new InvalidOperationException("Injected failure.");
            }
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync(Bank));

        Assert.Equal(3, saves);
        Assert.Equal((0, 0, 0), await CountsAsync(database));
    }

    [Fact]
    public async Task The_command_prints_one_line_and_exits_0_when_it_seeds_or_skips()
    {
        var database = await Postgres.CreateDatabaseAsync();
        await using var services = CommandServices(database);

        var (firstCode, firstOut, firstError) = await RunCommandAsync(services, SeedFileTests.BankPath());
        var (secondCode, secondOut, _) = await RunCommandAsync(services, SeedFileTests.BankPath());

        Assert.Equal(0, firstCode);
        Assert.Equal("Applied 0 migrations. Seeded 1 subject, 6 chapters and 72 questions.", firstOut.Trim());
        Assert.Empty(firstError);
        Assert.Equal(0, secondCode);
        Assert.Single(secondOut.Trim().Split('\n'));
        Assert.Contains("nothing was changed", secondOut);
        Assert.Equal((1, 6, 72), await CountsAsync(database));
    }

    [Fact]
    public async Task The_command_exits_non_zero_on_a_bad_or_missing_file_and_seeds_nothing()
    {
        var database = await Postgres.CreateDatabaseAsync();
        await using var services = CommandServices(database);
        var badFile = Path.Combine(Path.GetTempPath(), $"bad-bank-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(badFile, """
            {"subjects":[{"name":"Physics","level":"O Level","chapters":[{"name":"Motion","orderIndex":1,
              "questions":[{"text":"Zero marks?","marks":0,"difficulty":"Easy","type":"Short"}]}]}]}
            """);

        try
        {
            var (badCode, _, badError) = await RunCommandAsync(services, badFile);
            var (missingCode, _, missingError) = await RunCommandAsync(services, badFile + ".missing");

            Assert.NotEqual(0, badCode);
            Assert.Contains("marks must be positive", badError);
            Assert.NotEqual(0, missingCode);
            Assert.NotEmpty(missingError);
            Assert.Equal((0, 0, 0), await CountsAsync(database));
        }
        finally
        {
            File.Delete(badFile);
        }
    }

    private static ServiceProvider CommandServices(TestDatabase database) =>
        new ServiceCollection()
            .AddSingleton<IDbContextFactory<ExamForgeDbContext>>(new TestDbContextFactory(database, null))
            .AddSingleton<TimeProvider>(new FakeTimeProvider())
            .AddScoped<QuestionBankSeeder>()
            .BuildServiceProvider();

    private static async Task<(int Code, string Output, string Error)> RunCommandAsync(IServiceProvider services, string path)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SeedCommand.RunAsync(services, output, error, path);
        return (code, output.ToString(), error.ToString());
    }
}
