using System.Net;
using ExamForge.Tests.Persistence;

namespace ExamForge.Tests.Api;

[Collection(PostgresCollection.Name)]
public class ListPapersApiTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Fact]
    public async Task Lists_only_the_subjects_papers_newest_first_with_id_breaking_ties()
    {
        var a = await SeedBankAsync([5]);
        var b = await SeedBankAsync([5]);
        var oldest = await SeedHistoryPaperAsync(a.SubjectId, Day(1));
        var tiedFirst = await SeedHistoryPaperAsync(a.SubjectId, Day(3));
        var tiedSecond = await SeedHistoryPaperAsync(a.SubjectId, Day(3)); // same time, higher Id
        var middle = await SeedHistoryPaperAsync(a.SubjectId, Day(2));
        await SeedHistoryPaperAsync(b.SubjectId, Day(5)); // newest overall, other subject

        var body = await JsonAsync(await Client.GetAsync($"/api/papers?subjectId={a.SubjectId}"), HttpStatusCode.OK);

        var papers = body.AsArray();
        Assert.Equal([tiedSecond, tiedFirst, middle, oldest], papers.Select(p => (int)p!["id"]!));
        Assert.All(papers, p => Assert.Equal(["createdAt", "id", "seed", "title", "totalMarks"], Keys(p!)));
    }

    [Fact]
    public async Task A_subject_with_no_papers_lists_nothing()
    {
        var bank = await SeedBankAsync([5]);

        var body = await JsonAsync(await Client.GetAsync($"/api/papers?subjectId={bank.SubjectId}"), HttpStatusCode.OK);

        Assert.Empty(body.AsArray());
    }

    [Fact]
    public async Task Listing_without_a_subject_id_is_400()
    {
        var body = await JsonAsync(await Client.GetAsync("/api/papers"), HttpStatusCode.BadRequest);

        Assert.True(body["errors"]!.AsObject().ContainsKey("subjectId"));
    }

    [Fact]
    public async Task Listing_an_unknown_subject_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/papers?subjectId=999999")).StatusCode);
    }
}
