using System.Text.Json;
using System.Text.Json.Serialization;
using ExamForge.Domain.Enums;

namespace ExamForge.Infrastructure.Seeding;

/// <summary>The contents of seed/questions.json: subjects, their chapters, and each chapter's questions.</summary>
public sealed record QuestionBankFile(IReadOnlyList<SeedSubject> Subjects)
{
    public const string RelativePath = "seed/questions.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    /// <summary>Walks up from <paramref name="startDirectory"/> to find seed/questions.json.</summary>
    public static string? Find(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, RelativePath);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Reads and checks the file. Any problem is an InvalidDataException naming the entry.</summary>
    public static QuestionBankFile Load(string path)
    {
        QuestionBankFile? bank;
        try
        {
            bank = JsonSerializer.Deserialize<QuestionBankFile>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not a valid question bank: {ex.Message}", ex);
        }

        if (bank?.Subjects is not { Count: > 0 })
        {
            throw new InvalidDataException($"{path} has no subjects.");
        }

        foreach (var subject in bank.Subjects)
        {
            Require(!string.IsNullOrWhiteSpace(subject.Name) && !string.IsNullOrWhiteSpace(subject.Level), "Every subject needs a name and a level.");
            Require(subject.Chapters is { Count: > 0 }, $"Subject '{subject.Name}' has no chapters.");
            Require(
                subject.Chapters.Select(c => c.OrderIndex).Distinct().Count() == subject.Chapters.Count,
                $"Subject '{subject.Name}' has two chapters with the same orderIndex.");

            foreach (var chapter in subject.Chapters)
            {
                Require(!string.IsNullOrWhiteSpace(chapter.Name) && chapter.OrderIndex >= 1, $"Chapter '{chapter.Name}' needs a name and an orderIndex of 1 or more.");
                Require(chapter.Questions is { Count: > 0 }, $"Chapter '{chapter.Name}' has no questions.");

                foreach (var question in chapter.Questions)
                {
                    Require(!string.IsNullOrWhiteSpace(question.Text), $"A question in '{chapter.Name}' has no text.");
                    Require(question.Marks > 0, $"A question in '{chapter.Name}' has {question.Marks} marks; marks must be positive.");
                    Require(Enum.IsDefined(question.Difficulty) && Enum.IsDefined(question.Type), $"A question in '{chapter.Name}' has no difficulty or type.");
                }
            }
        }

        return bank;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}

public sealed record SeedSubject(string Name, string Level, IReadOnlyList<SeedChapter> Chapters);

public sealed record SeedChapter(string Name, int OrderIndex, IReadOnlyList<SeedQuestion> Questions);

public sealed record SeedQuestion(string Text, int Marks, Difficulty Difficulty, QuestionType Type);
