namespace ExamForge.Domain.Generation;

/// <summary>
/// Picks a seed when a request has none. This is the only non-seeded randomness allowed, and
/// it is used once per request, before any solving. Injected so tests can control it.
/// </summary>
public interface ISeedSource
{
    long NextSeed();
}
