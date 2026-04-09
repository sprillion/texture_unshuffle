using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TextureUnshuffle.Models.UvAnalysis;

namespace TextureUnshuffle.Models.Solvers;

/// <summary>
/// Edge-matching solver guided by the structural adjacency from UV analysis.
///
/// How it differs from pure EdgeMatchingSolver:
///   The UV adjacency graph encodes which display positions are adjacent in the
///   model's UV layout (i.e. their edges must be seamless in the original texture).
///   Those position pairs are used as *additional* edge-comparison sources on top of
///   the normal grid neighbours, so tiles that belong at UV-connected positions are
///   scored more accurately.
///
///   NOTE: UV graph values are *position indices* (row*cols+col), NOT tile indices.
///         Treating them as forced tile indices would be wrong for shuffled textures.
///         This implementation therefore never forces a specific tile; it only extends
///         the set of edge comparisons used during greedy placement.
/// </summary>
public class UvConstrainedSolver : IArrangementSolver
{
    private const float NormalisationMse = 50f;
    private const int   MaxRestarts      = 8;

    private readonly TileAdjacencyGraph _graph;

    public UvConstrainedSolver(TileAdjacencyGraph graph) => _graph = graph;

    public Task<SolverResult> SolveAsync(
        SKBitmap[] tiles, int cols, int rows,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
        => Task.Run(() => Solve(tiles, cols, rows, progress, ct), ct);

    private SolverResult Solve(
        SKBitmap[] tiles, int cols, int rows,
        IProgress<int>? progress, CancellationToken ct)
    {
        int n = cols * rows;

        // ── Build pairwise edge-score matrices ─────────────────────────────
        float[,] horizScore = new float[n, n];
        float[,] vertScore  = new float[n, n];

        for (int a = 0; a < n; a++)
        {
            ct.ThrowIfCancellationRequested();
            for (int b = 0; b < n; b++)
            {
                if (a == b)
                {
                    horizScore[a, b] = float.MaxValue;
                    vertScore[a, b]  = float.MaxValue;
                    continue;
                }
                horizScore[a, b] = EdgeMatchingSolver.ComputeEdgeScore(tiles[a], tiles[b], horizontal: true);
                vertScore[a, b]  = EdgeMatchingSolver.ComputeEdgeScore(tiles[a], tiles[b], horizontal: false);
            }
            progress?.Report(a * 60 / n); // 0–60 % for matrix
        }

        // ── Build inverse UV lookups: for each position, which position is its UV-left / UV-above ──
        // UV graph: RightPairs gives (leftPos, rightPos) → rightPos has leftPos to its left.
        // We use these so that when filling position rightPos, we compare against the tile
        // already placed at leftPos (even if leftPos != rightPos-1 in grid terms).
        var uvLeftOf  = new Dictionary<int, int>(); // uvLeftOf[b]  = a  (position a is UV-left of b)
        var uvAboveOf = new Dictionary<int, int>(); // uvAboveOf[b] = a  (position a is UV-above of b)

        foreach (var (left, right) in _graph.RightPairs)
            uvLeftOf[right] = left;
        foreach (var (top, bottom) in _graph.BelowPairs)
            uvAboveOf[bottom] = top;

        // ── Multi-restart greedy ────────────────────────────────────────────
        int[]? bestArrangement = null;
        float  bestConfidence  = -1f;
        int    restarts        = Math.Min(MaxRestarts, n);

        for (int attempt = 0; attempt < restarts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            int[] arr = SolveGreedy(horizScore, vertScore, uvLeftOf, uvAboveOf, n, cols, rows, attempt);
            float conf = ComputeConfidence(arr, horizScore, vertScore, cols, rows);
            if (conf > bestConfidence) { bestConfidence = conf; bestArrangement = arr; }
            progress?.Report(60 + (attempt + 1) * 40 / restarts); // 60–100 %
        }

        progress?.Report(100);
        return new SolverResult { Arrangement = bestArrangement!, Confidence = bestConfidence };
    }

    // ── Single greedy pass ──────────────────────────────────────────────────
    private static int[] SolveGreedy(
        float[,] horizScore, float[,] vertScore,
        Dictionary<int, int> uvLeftOf, Dictionary<int, int> uvAboveOf,
        int n, int cols, int rows, int firstTile)
    {
        int[] arrangement = new int[n];
        bool[] used       = new bool[n];

        // Pin first tile
        arrangement[0] = firstTile;
        used[firstTile] = true;

        for (int pos = 1; pos < n; pos++)
        {
            int r = pos / cols;
            int c = pos % cols;

            // Determine which already-placed positions provide edge references.
            // Prefer UV-specified neighbours; fall back to grid neighbours.
            int leftTile = -1, topTile = -1;

            if (c > 0)
            {
                // Grid-left is always available; UV-left might override if it was placed earlier
                leftTile = arrangement[pos - 1];
                if (uvLeftOf.TryGetValue(pos, out int uvLp) && uvLp < pos)
                    leftTile = arrangement[uvLp]; // UV-specified left wins if already filled
            }
            else if (uvLeftOf.TryGetValue(pos, out int uvLp) && uvLp < pos)
            {
                leftTile = arrangement[uvLp];
            }

            if (r > 0)
            {
                topTile = arrangement[pos - cols];
                if (uvAboveOf.TryGetValue(pos, out int uvAp) && uvAp < pos)
                    topTile = arrangement[uvAp];
            }
            else if (uvAboveOf.TryGetValue(pos, out int uvAp) && uvAp < pos)
            {
                topTile = arrangement[uvAp];
            }

            // Pick unused tile with minimum edge score
            float best = float.MaxValue;
            int tile = -1;
            for (int t = 0; t < n; t++)
            {
                if (used[t]) continue;
                float score = 0f;
                if (leftTile >= 0) score += horizScore[leftTile, t];
                if (topTile  >= 0) score += vertScore[topTile,  t];
                if (score < best) { best = score; tile = t; }
            }

            arrangement[pos] = tile;
            used[tile] = true;
        }

        return arrangement;
    }

    // ── Confidence calculation ──────────────────────────────────────────────
    private static float ComputeConfidence(
        int[] arrangement, float[,] horizScore, float[,] vertScore, int cols, int rows)
    {
        float totalScore   = 0f;
        int   boundaryCount = 0;
        int   n            = cols * rows;
        for (int pos = 0; pos < n; pos++)
        {
            int r = pos / cols;
            int c = pos % cols;
            if (c + 1 < cols)
            {
                totalScore += horizScore[arrangement[pos], arrangement[pos + 1]];
                boundaryCount++;
            }
            if (r + 1 < rows)
            {
                totalScore += vertScore[arrangement[pos], arrangement[pos + cols]];
                boundaryCount++;
            }
        }
        float avgScore = boundaryCount > 0 ? totalScore / boundaryCount : 0f;
        return Math.Clamp(1f - avgScore / NormalisationMse, 0f, 1f);
    }
}
