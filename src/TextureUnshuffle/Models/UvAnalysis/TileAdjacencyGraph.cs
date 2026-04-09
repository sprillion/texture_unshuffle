using System.Collections.Generic;
using System.Linq;

namespace TextureUnshuffle.Models.UvAnalysis;

/// <summary>
/// Stores directional adjacency constraints between tile indices derived from UV analysis.
/// Convention: RightOf[leftTile] = rightTile means <c>rightTile</c> sits immediately
/// to the right of <c>leftTile</c> in the original texture.
/// </summary>
public class TileAdjacencyGraph
{
    private readonly Dictionary<int, int> _rightOf = new();
    private readonly Dictionary<int, int> _belowOf = new();

    public int TileCount { get; }

    public TileAdjacencyGraph(int tileCount) => TileCount = tileCount;

    /// <summary>Records that <paramref name="rightTile"/> is immediately right of <paramref name="leftTile"/>.</summary>
    public void AddRightConstraint(int leftTile, int rightTile)
        => _rightOf[leftTile] = rightTile;

    /// <summary>Records that <paramref name="bottomTile"/> is immediately below <paramref name="topTile"/>.</summary>
    public void AddBelowConstraint(int topTile, int bottomTile)
        => _belowOf[topTile] = bottomTile;

    public bool TryGetRight(int tile, out int rightTile)
        => _rightOf.TryGetValue(tile, out rightTile);

    public bool TryGetBelow(int tile, out int bottomTile)
        => _belowOf.TryGetValue(tile, out bottomTile);

    public int ConstraintCount => _rightOf.Count + _belowOf.Count;

    /// <summary>All (leftPos, rightPos) pairs where rightPos is immediately right of leftPos.</summary>
    public IEnumerable<(int Left, int Right)> RightPairs
        => _rightOf.Select(kv => (kv.Key, kv.Value));

    /// <summary>All (topPos, bottomPos) pairs where bottomPos is immediately below topPos.</summary>
    public IEnumerable<(int Top, int Bottom)> BelowPairs
        => _belowOf.Select(kv => (kv.Key, kv.Value));
}
