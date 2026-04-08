using System;

namespace TextureUnshuffle.Models;

/// <summary>
/// Generates tile shuffle permutations using Fisher-Yates with a mulberry32 PRNG.
/// NOTE: The original HTML tool hardcodes the permutation for seed 149 without
/// exposing the generation algorithm. This implementation uses mulberry32 as a
/// reasonable guess — results for other seeds may differ from the original tool.
/// </summary>
public static class ShufflePermutation
{
    /// <summary>
    /// Generates a Fisher-Yates shuffle permutation for <paramref name="n"/> tiles
    /// using <paramref name="seed"/> with the mulberry32 PRNG.
    /// <c>perm[displayPos] = tileIndex</c> (i.e. "what tile appears at each position").
    /// </summary>
    public static int[] GeneratePermutation(int seed, int n)
    {
        var arr = new int[n];
        for (int i = 0; i < n; i++) arr[i] = i;

        uint s = (uint)seed;
        for (int i = n - 1; i > 0; i--)
        {
            double r = NextDouble(ref s);
            int j = (int)(r * (i + 1));
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }

        return arr;
    }

    /// <summary>
    /// Computes the inverse of a permutation.
    /// If <c>perm[pos] = tileIdx</c>, then <c>inv[tileIdx] = pos</c>.
    /// </summary>
    public static int[] GetInverse(int[] perm)
    {
        var inv = new int[perm.Length];
        for (int i = 0; i < perm.Length; i++)
            inv[perm[i]] = i;
        return inv;
    }

    // mulberry32 — returns a value in [0, 1)
    private static double NextDouble(ref uint s)
    {
        s += 0x6D2B79F5u;
        uint t = (s ^ (s >> 15)) * (1u | s);
        t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
        return ((t ^ (t >> 14)) & 0xFFFFFFFF) / 4294967296.0;
    }
}
