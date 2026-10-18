namespace ExamForge.Domain.Generation;

public static class ListExtensions
{
    /// <summary>
    /// Fisher-Yates shuffle in place, walking from the end of the list. Uses only the
    /// supplied random source, so the same seed always gives the same order.
    /// </summary>
    public static void Shuffle<T>(this IList<T> list, IRandomSource random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
