using System.Text.Json.Nodes;
using ExamForge.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace ExamForge.Tests.EndToEnd;

// 300 random but reproducible generate requests against the real seed bank (two copies of it, as
// two subjects), each response checked against properties the API must always hold.
[Collection(PostgresCollection.Name)]
public class GenerationPropertyTests(PostgresFixture postgres, ITestOutputHelper output) : EndToEndTestBase(postgres)
{
    private const int RequestCount = 300;
    private const int RandomSeed = 20261018;

    [Fact]
    public async Task Random_specs_never_yield_a_wrong_paper_or_a_false_422()
    {
        await SeedRealBankAsync();
        await AddSecondCopyOfBankAsync();
        var subjects = await LoadBankAsync();
        var questionsById = subjects.SelectMany(s => s.Chapters).SelectMany(c => c.Questions).ToDictionary(q => q.Id);

        var random = new Random(RandomSeed);
        var problems = new List<string>();
        var created = 0;
        var infeasibleByConstraint = new Dictionary<string, int> { ["chapterDistribution"] = 0, ["exactMarks"] = 0 };
        var infeasibleResponses = 0;

        for (var n = 1; n <= RequestCount; n++)
        {
            // Mostly the first subject, so its history builds up; sometimes the second.
            var spec = NextSpec(random, random.Next(4) == 0 ? subjects[1] : subjects[0]);
            var history = await HistoryAsync(spec.Subject.Id, spec.ExcludeLastNPapers);
            var papersBefore = await PaperCountAsync(spec.Subject.Id);

            var (status, body, text) = await ReadAsync(await PostGenerateAsync(spec.Body));
            var label = $"#{n} {spec.Body.ToJsonString()}";

            switch (status)
            {
                case 201:
                    created++;
                    CheckPaper(spec, body!, history, questionsById, label, problems);
                    Expect(await PaperCountAsync(spec.Subject.Id) == papersBefore + 1, $"{label}: 201 but no new paper row", problems);
                    break;

                case 422:
                    infeasibleResponses++;
                    CheckInfeasible(spec, body!, history, label, problems, infeasibleByConstraint);
                    Expect(await PaperCountAsync(spec.Subject.Id) == papersBefore, $"{label}: 422 but a paper was saved", problems);
                    break;

                default:
                    problems.Add($"{label}: unexpected {status}: {text}");
                    break;
            }
        }

        output.WriteLine(
            $"{RequestCount} requests: {created} x 201, {infeasibleResponses} x 422 "
            + $"(chapter failures: {infeasibleByConstraint["chapterDistribution"]} chapterDistribution, "
            + $"{infeasibleByConstraint["exactMarks"]} exactMarks)");

        Assert.True(problems.Count == 0, $"{problems.Count} property violations:\n{string.Join("\n", problems.Take(10))}");

        // Both outcomes must be well represented, or the run proves little about one of them.
        Assert.InRange(created, RequestCount / 5, RequestCount * 4 / 5);
        Assert.True(infeasibleByConstraint.Values.All(count => count > 0), "Both 422 constraint types should occur.");
    }

    private static void CheckPaper(
        RandomSpec spec,
        JsonNode body,
        HashSet<int> history,
        Dictionary<int, BankQuestion> questionsById,
        string label,
        List<string> problems)
    {
        var questions = body["questions"]!.AsArray().Select(q => q!).ToList();
        var ids = questions.Select(q => (int)q["questionId"]!).ToList();

        Expect((int)body["totalMarks"]! == spec.TotalMarks, $"{label}: totalMarks {body["totalMarks"]} != {spec.TotalMarks}", problems);
        Expect(questions.Sum(q => (int)q["marks"]!) == spec.TotalMarks, $"{label}: marks sum to {questions.Sum(q => (int)q["marks"]!)}, not {spec.TotalMarks}", problems);

        foreach (var (chapterId, marks) in spec.Distribution)
        {
            var chapterMarks = questions.Where(q => (int)q["chapterId"]! == chapterId).Sum(q => (int)q["marks"]!);
            Expect(chapterMarks == marks, $"{label}: chapter {chapterId} has {chapterMarks} marks, requested {marks}", problems);
        }

        Expect(questions.All(q => spec.Distribution.Any(d => d.ChapterId == (int)q["chapterId"]!)), $"{label}: a question from an unrequested chapter", problems);
        Expect(questions.Select(q => (int)q["orderIndex"]!).SequenceEqual(Enumerable.Range(1, questions.Count)), $"{label}: OrderIndex is not 1..n", problems);
        Expect(ids.Distinct().Count() == ids.Count, $"{label}: a question appears twice", problems);

        var reused = ids.Where(history.Contains).ToList();
        Expect(reused.Count == 0, $"{label}: questions {string.Join(",", reused)} were used in the last {spec.ExcludeLastNPapers} papers", problems);

        foreach (var question in questions)
        {
            var stored = questionsById[(int)question["questionId"]!];
            Expect(stored.IsActive, $"{label}: question {stored.Id} is inactive", problems);
            Expect(stored.Marks == (int)question["marks"]!, $"{label}: question {stored.Id} reported with {question["marks"]} marks, stored {stored.Marks}", problems);
            Expect(stored.ChapterId == (int)question["chapterId"]!, $"{label}: question {stored.Id} reported in the wrong chapter", problems);
        }
    }

    private static void CheckInfeasible(
        RandomSpec spec,
        JsonNode body,
        HashSet<int> history,
        string label,
        List<string> problems,
        Dictionary<string, int> byConstraint)
    {
        Expect((string?)body["error"] == "INFEASIBLE_SPECIFICATION", $"{label}: 422 error is {body["error"]}", problems);
        var reported = body["failures"]!.AsArray().Select(f => f!).ToDictionary(f => (int)f["chapterId"]!);

        foreach (var (chapterId, requested) in spec.Distribution)
        {
            var chapter = spec.Subject.Chapters.Single(c => c.Id == chapterId);
            var eligible = chapter.Questions.Where(q => q.IsActive && !history.Contains(q.Id)).Select(q => q.Marks).ToList();
            var available = eligible.Sum();
            var feasible = SomeSubsetSumsTo(eligible, requested);

            if (reported.TryGetValue(chapterId, out var failure))
            {
                var constraint = (string)failure["constraint"]!;
                byConstraint[constraint] = byConstraint.GetValueOrDefault(constraint) + 1;

                Expect(!feasible, $"{label}: chapter {chapterId} reported infeasible, but a subset of {string.Join(",", eligible)} makes {requested}", problems);
                Expect((int)failure["requestedMarks"]! == requested, $"{label}: chapter {chapterId} requestedMarks {failure["requestedMarks"]} != {requested}", problems);
                Expect((int)failure["availableMarks"]! == available, $"{label}: chapter {chapterId} availableMarks {failure["availableMarks"]} != {available}", problems);
                Expect(
                    constraint == (available < requested ? "chapterDistribution" : "exactMarks"),
                    $"{label}: chapter {chapterId} constraint {constraint} with {available} available for {requested}",
                    problems);
            }
            else
            {
                Expect(feasible, $"{label}: chapter {chapterId} not reported, but no subset of {string.Join(",", eligible)} makes {requested}", problems);
            }
        }

        Expect(reported.Keys.All(id => spec.Distribution.Any(d => d.ChapterId == id)), $"{label}: a reported chapter was not requested", problems);
    }

    private static void Expect(bool condition, string problem, List<string> problems)
    {
        if (!condition)
        {
            problems.Add(problem);
        }
    }
}
