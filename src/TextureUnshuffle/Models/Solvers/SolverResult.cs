namespace TextureUnshuffle.Models.Solvers;

public class SolverResult
{
    /// <summary>arrangement[displayPos] = tileIndex — same convention as TileGrid.Arrangement</summary>
    public required int[] Arrangement { get; init; }

    /// <summary>
    /// Optional. rotations[displayPos] = 0–3 (90° CW turns).
    /// Null means all tiles are unrotated.
    /// </summary>
    public int[]? Rotations { get; init; }

    /// <summary>
    /// Optional. flips[displayPos] — bitmask: bit 0 = flip H, bit 1 = flip V (applied after rotation).
    /// Null means no flips.
    /// </summary>
    public int[]? Flips { get; init; }

    /// <summary>0..1, where 1 = perfect edge continuity, 0 = no confidence</summary>
    public float Confidence { get; init; }
}
