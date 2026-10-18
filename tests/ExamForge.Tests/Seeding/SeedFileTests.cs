using System.Text.Json;
using System.Text.RegularExpressions;
using ExamForge.Domain.Enums;
using ExamForge.Infrastructure.Seeding;

namespace ExamForge.Tests.Seeding;

// The committed question bank, checked as data: shape, spread of marks and difficulty, and the
// formatting conventions for each question type.
public partial class SeedFileTests
{
    internal static string BankPath() =>
        QuestionBankFile.Find(AppContext.BaseDirectory)
        ?? throw new FileNotFoundException($"{QuestionBankFile.RelativePath} not found above {AppContext.BaseDirectory}");

    private static readonly Lazy<QuestionBankFile> Bank = new(() => QuestionBankFile.Load(BankPath()));

    private static IEnumerable<SeedQuestion> AllQuestions => Bank.Value.Subjects.SelectMany(s => s.Chapters).SelectMany(c => c.Questions);

    [Fact]
    public void The_bank_is_one_o_level_physics_subject_with_six_chapters_of_twelve_questions()
    {
        var subject = Assert.Single(Bank.Value.Subjects);
        Assert.Equal("Physics", subject.Name);
        Assert.Equal("O Level", subject.Level);
        Assert.Equal([1, 2, 3, 4, 5, 6], subject.Chapters.Select(c => c.OrderIndex));
        Assert.All(subject.Chapters, c => Assert.Equal(12, c.Questions.Count));
        Assert.Equal(72, AllQuestions.Count());
    }

    [Fact]
    public void Every_question_has_positive_marks_and_enum_names_written_out()
    {
        Assert.All(AllQuestions, q => Assert.True(q.Marks > 0));

        // Checked on the raw JSON too, so a number like "difficulty": 2 cannot slip through.
        using var document = JsonDocument.Parse(File.ReadAllText(BankPath()));
        var raw = document.RootElement.GetProperty("subjects").EnumerateArray()
            .SelectMany(s => s.GetProperty("chapters").EnumerateArray())
            .SelectMany(c => c.GetProperty("questions").EnumerateArray())
            .ToList();
        Assert.All(raw, q =>
        {
            Assert.Contains(q.GetProperty("difficulty").GetString(), Enum.GetNames<Difficulty>());
            Assert.Contains(q.GetProperty("type").GetString(), Enum.GetNames<QuestionType>());
        });
    }

    [Fact]
    public void Each_chapter_has_small_marks_for_exact_targets_and_every_difficulty()
    {
        Assert.All(Bank.Value.Subjects.SelectMany(s => s.Chapters), chapter =>
        {
            Assert.True(chapter.Questions.Count(q => q.Marks == 1) >= 3, $"{chapter.Name}: fewer than three 1-mark questions");
            Assert.True(chapter.Questions.Count(q => q.Marks == 2) >= 3, $"{chapter.Name}: fewer than three 2-mark questions");
            Assert.True(chapter.Questions.Count(q => q.Marks == 3) >= 2, $"{chapter.Name}: fewer than two 3-mark questions");
            Assert.All(Enum.GetValues<Difficulty>(), d => Assert.Contains(chapter.Questions, q => q.Difficulty == d));
        });
    }

    [Fact]
    public void No_two_questions_share_the_same_text()
    {
        var duplicates = AllQuestions
            .GroupBy(q => q.Text.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void The_type_follows_the_marks()
    {
        Assert.All(AllQuestions, q => Assert.Equal(
            q.Marks switch { 1 => QuestionType.Mcq, 2 or 3 => QuestionType.Short, _ => QuestionType.Structured },
            q.Type));
    }

    [Fact]
    public void Mcq_questions_list_options_a_to_d_in_their_text()
    {
        Assert.All(AllQuestions.Where(q => q.Type == QuestionType.Mcq), q =>
        {
            var options = q.Text.Split('\n').Where(line => OptionLine().IsMatch(line)).Select(line => line[0]).ToList();
            Assert.True(options.SequenceEqual(['A', 'B', 'C', 'D']), $"Options A-D not found in: {q.Text}");
        });
    }

    [Fact]
    public void Structured_questions_have_parts_whose_marks_add_up()
    {
        Assert.All(AllQuestions.Where(q => q.Type == QuestionType.Structured), q =>
        {
            Assert.Contains("(a)", q.Text);
            Assert.Contains("(b)", q.Text);
            var partMarks = PartMarks().Matches(q.Text).Sum(m => int.Parse(m.Groups[1].Value));
            Assert.True(partMarks == q.Marks, $"Parts add up to {partMarks}, question has {q.Marks}: {q.Text}");
        });
    }

    [GeneratedRegex(@"^[A-D]\. \S")]
    private static partial Regex OptionLine();

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex PartMarks();
}
