namespace ExamForge.Domain.Generation;

/// <summary>
/// SplitMix64. Chosen over System.Random because its output is fixed by this code,
/// not by the .NET version, and because it takes a 64-bit seed.
/// </summary>
public sealed class SplitMix64Random : IRandomSource
{
    private ulong _state;

    public SplitMix64Random(long seed)
    {
        // Reinterpret the signed seed's bits as unsigned; a negative seed is valid.
        _state = unchecked((ulong)seed);
    }

    public ulong NextUInt64()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <remarks>
    /// Uses <c>NextUInt64() % n</c>. That is slightly biased towards small values when n
    /// does not divide 2^64, but for n in the hundreds the bias is about 1 in 10^17,
    /// far below anything that matters here.
    /// </remarks>
    public int NextInt(int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        return (int)(NextUInt64() % (ulong)n);
    }
}
