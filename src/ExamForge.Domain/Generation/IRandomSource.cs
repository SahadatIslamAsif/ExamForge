namespace ExamForge.Domain.Generation;

/// <summary>
/// The only source of randomness allowed in the selection path. One instance is
/// created per generation request from the request's seed.
/// </summary>
public interface IRandomSource
{
    ulong NextUInt64();

    /// <summary>Returns a value in [0, n). Throws if n is not positive.</summary>
    int NextInt(int n);
}
