using ExamForge.Domain.Generation;

namespace ExamForge.Api.Papers;

/// <summary>
/// The 400 rules from SPEC's validation list, checked before any database access. Errors are
/// keyed by the JSON path of the offending field. Chapter ownership is not here: it needs the
/// database, so the generation service reports it.
/// </summary>
public static class GeneratePaperRequestValidator
{
    private const double ShareTolerance = 1e-6;

    public static Dictionary<string, List<string>> Validate(GeneratePaperRequest request)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var messages))
            {
                errors[field] = messages = [];
            }

            messages.Add(message);
        }

        if (request.SubjectId is null)
        {
            Add("subjectId", "subjectId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            Add("title", "title is required and must not be blank.");
        }

        if (request.TotalMarks is null)
        {
            Add("totalMarks", "totalMarks is required.");
        }
        else if (request.TotalMarks <= 0)
        {
            Add("totalMarks", "totalMarks must be positive.");
        }

        ValidateDistribution(request, Add);
        ValidateMix(request.DifficultyMix, Add);

        // Required, with no default: silently treating a missing value as 0 would switch off
        // history exclusion without the client noticing.
        if (request.ExcludeLastNPapers is null)
        {
            Add("excludeLastNPapers", "excludeLastNPapers is required (send 0 to disable the history window).");
        }
        else if (request.ExcludeLastNPapers < 0)
        {
            Add("excludeLastNPapers", "excludeLastNPapers must be 0 or greater (0 disables the history window).");
        }

        if (request.Seed is < -SeedRange.MaxSafeInteger or > SeedRange.MaxSafeInteger)
        {
            Add("seed",
                $"seed must be between -{SeedRange.MaxSafeInteger} and {SeedRange.MaxSafeInteger}. "
                + "That is JavaScript's safe integer range (Number.MAX_SAFE_INTEGER): JavaScript reads JSON "
                + "numbers as doubles, so a larger seed could be changed in transit and no longer reproduce the paper.");
        }

        return errors;
    }

    /// <summary>Converts a request that passed <see cref="Validate"/> into the domain specification.</summary>
    public static PaperSpecification ToSpecification(GeneratePaperRequest request) =>
        new(
            request.SubjectId!.Value,
            request.Title!.Trim(),
            request.TotalMarks!.Value,
            request.ChapterDistribution!.Select(c => new ChapterMarks(c!.ChapterId!.Value, c.Marks!.Value)).ToList(),
            new DifficultyMix(request.DifficultyMix!.Easy!.Value, request.DifficultyMix.Medium!.Value, request.DifficultyMix.Hard!.Value),
            request.ExcludeLastNPapers!.Value,
            request.Seed);

    private static void ValidateDistribution(GeneratePaperRequest request, Action<string, string> add)
    {
        var distribution = request.ChapterDistribution;
        if (distribution is null || distribution.Count == 0)
        {
            add("chapterDistribution", "chapterDistribution must list at least one chapter.");
            return;
        }

        var entriesValid = true;
        var seen = new HashSet<int>();
        for (var i = 0; i < distribution.Count; i++)
        {
            var entry = distribution[i];
            if (entry is null)
            {
                add($"chapterDistribution[{i}]", "Entry must not be null.");
                entriesValid = false;
                continue;
            }

            if (entry.ChapterId is null)
            {
                add($"chapterDistribution[{i}].chapterId", "chapterId is required.");
                entriesValid = false;
            }
            else if (!seen.Add(entry.ChapterId.Value))
            {
                add($"chapterDistribution[{i}].chapterId", $"Chapter {entry.ChapterId} appears more than once.");
                entriesValid = false;
            }

            if (entry.Marks is null or <= 0)
            {
                add($"chapterDistribution[{i}].marks", "marks must be positive.");
                entriesValid = false;
            }
        }

        // Only meaningful once every entry has positive marks and a total exists.
        if (entriesValid && request.TotalMarks is > 0)
        {
            var sum = distribution.Sum(c => c!.Marks!.Value);
            if (sum != request.TotalMarks)
            {
                add("chapterDistribution", $"Chapter marks sum to {sum}, but totalMarks is {request.TotalMarks}.");
            }
        }
    }

    private static void ValidateMix(DifficultyMixRequest? mix, Action<string, string> add)
    {
        if (mix is null)
        {
            add("difficultyMix", "difficultyMix is required.");
            return;
        }

        var sharesValid = true;
        foreach (var (name, share) in new[] { ("easy", mix.Easy), ("medium", mix.Medium), ("hard", mix.Hard) })
        {
            if (share is null)
            {
                add($"difficultyMix.{name}", $"difficultyMix.{name} is required.");
                sharesValid = false;
            }
            else if (share is < 0 or > 1)
            {
                add($"difficultyMix.{name}", $"difficultyMix.{name} must be between 0 and 1.");
                sharesValid = false;
            }
        }

        if (sharesValid && Math.Abs(mix.Easy!.Value + mix.Medium!.Value + mix.Hard!.Value - 1.0) > ShareTolerance)
        {
            add("difficultyMix", "Difficulty shares must sum to 1.");
        }
    }
}
