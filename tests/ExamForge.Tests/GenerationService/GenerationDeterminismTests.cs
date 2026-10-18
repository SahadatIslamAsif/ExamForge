using ExamForge.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamForge.Tests.GenerationService;

// ExcludeLastNPapers is 0 throughout: every generation adds to the history, and a
// comparison only makes sense when the second run sees the same eligible pool.
[Collection(PostgresCollection.Name)]
public class GenerationDeterminismTests(PostgresFixture postgres) : GenerationTestBase(postgres)
{
    // 30 questions of 1-3 marks: many different ways to make 20.
    private static readonly int[] AmpleChapter = Enumerable.Range(0, 30).Select(i => i % 3 + 1).ToArray();

    [Fact]
    public async Task Same_spec_and_seed_give_the_same_questions_in_the_same_order()
    {
        var bank = await SeedBankAsync(AmpleChapter, AmpleChapter);
        var spec = Spec(bank, [20, 15], seed: 20261018);

        var first = AssertSuccess(await NewService().GenerateAsync(spec));
        var second = AssertSuccess(await NewService().GenerateAsync(spec));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(Ids(first), Ids(second));
    }

    [Fact]
    public async Task Different_seeds_give_different_selections_on_an_ample_pool()
    {
        var bank = await SeedBankAsync(AmpleChapter);

        var one = AssertSuccess(await NewService().GenerateAsync(Spec(bank, [20], seed: 1)));
        var two = AssertSuccess(await NewService().GenerateAsync(Spec(bank, [20], seed: 2)));

        Assert.NotEqual(Ids(one), Ids(two));
    }

    [Fact]
    public async Task An_omitted_seed_is_generated_stored_returned_and_reproduces_the_paper()
    {
        var bank = await SeedBankAsync(AmpleChapter);
        var seeds = new SequenceSeedSource(777);

        var generated = AssertSuccess(await NewService(seeds).GenerateAsync(Spec(bank, [20], seed: null)));

        Assert.Equal(777, generated.Seed);
        Assert.Equal(1, seeds.Calls);
        await using (var context = NewContext())
        {
            Assert.Equal(777, (await context.Papers.AsNoTracking().SingleAsync(p => p.Id == generated.Id)).Seed);
        }

        var replayed = AssertSuccess(await NewService().GenerateAsync(Spec(bank, [20], seed: generated.Seed)));
        Assert.Equal(Ids(generated), Ids(replayed));
    }
}
