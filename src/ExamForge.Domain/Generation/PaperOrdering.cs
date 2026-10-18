namespace ExamForge.Domain.Generation;

public static class PaperOrdering
{
    /// <summary>
    /// Flattens the per-chapter selections into the paper's order: chapter OrderIndex (not chapter
    /// Id), then difficulty ascending (by the enum's explicit values), then question Id. Question
    /// Ids are unique, so this is a total order and the input order never matters.
    /// </summary>
    public static IReadOnlyList<OrderedQuestion> Order(
        IReadOnlyList<ChapterRequest> requests,
        IReadOnlyList<ChapterSelection> chapters)
    {
        var requestsById = requests.ToDictionary(r => r.ChapterId);

        return chapters
            .SelectMany(chapter =>
            {
                var request = requestsById.TryGetValue(chapter.ChapterId, out var found)
                    ? found
                    : throw new InvalidOperationException(
                        $"Solver bug: selection for chapter {chapter.ChapterId} has no matching request.");
                return chapter.Questions.Select(question => (Request: request, Question: question));
            })
            .OrderBy(x => x.Request.OrderIndex)
            .ThenBy(x => (int)x.Question.Difficulty)
            .ThenBy(x => x.Question.Id)
            .Select((x, i) => new OrderedQuestion(
                OrderIndex: i + 1,
                QuestionId: x.Question.Id,
                ChapterId: x.Request.ChapterId,
                ChapterName: x.Request.ChapterName,
                Marks: x.Question.Marks,
                Difficulty: x.Question.Difficulty))
            .ToList();
    }
}
