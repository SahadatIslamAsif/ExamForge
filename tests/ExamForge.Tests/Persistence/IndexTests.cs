namespace ExamForge.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public class IndexTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    // This index serves the "N most recent papers for a subject" query, so its column order
    // and sort directions matter, not just its existence.
    [Fact]
    public async Task Paper_history_index_is_subject_then_created_at_desc_then_id_desc()
    {
        var definition = await ScalarAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_Papers_SubjectId_CreatedAt_Id'");

        Assert.EndsWith("(\"SubjectId\", \"CreatedAt\" DESC, \"Id\" DESC)", definition);
    }
}
