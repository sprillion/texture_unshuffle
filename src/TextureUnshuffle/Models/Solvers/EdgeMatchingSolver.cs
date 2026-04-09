using SkiaSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TextureUnshuffle.Models.Solvers;

/// <summary>
/// Greedy edge-matching solver. Fills positions left-to-right, top-to-bottom,
/// picking at each step the unused tile that minimises the MSE along the shared
/// edges with already-placed neighbours.
/// Runs multiple restarts (each starting with a different first tile) and
/// returns the arrangement with the best overall edge continuity.
/// </summary>
public class EdgeMatchingSolver : IArrangementSolver
{
    // Typical RGB MSE of a "random" edge pair — used to normalise confidence.
    private const float NormalisationMse = 50f;

    // How many different starting tiles to try. Capped at n to avoid redundancy.
    private const int MaxRestarts = 8;

    public Task<SolverResult> SolveAsync(
        SKBitmap[] tiles, int cols, int rows,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
        => Task.Run(() => Solve(tiles, cols, rows, progress, ct), ct);

    private static SolverResult Solve(
        SKBitmap[] tiles, int cols, int rows,
        IProgress<int>? progress, CancellationToken ct)
    {
        int n = cols * rows;

        // ── Build pairwise edge-score matrices ─────────────────────────────
        // horizScore[a, b] = MSE of a's right edge vs b's left edge
        // vertScore[a, b]  = MSE of a's bottom edge vs b's top edge
        float[,] horizScore = new float[n, n];
        float[,] vertScore = new float[n, n];

        for (int a = 0; a < n; a++)
        {
            ct.ThrowIfCancellationRequested();
            for (int b = 0; b < n; b++)
            {
                if (a == b)
                {
                    horizScore[a, b] = float.MaxValue;
                    vertScore[a, b] = float.MaxValue;
                    continue;
                }
                horizScore[a, b] = ComputeEdgeScore(tiles[a], tiles[b], horizontal: true);
                vertScore[a, b] = ComputeEdgeScore(tiles[a], tiles[b], horizontal: false);
            }
            progress?.Report(a * 40 / n); // 0–40 % for matrix building
        }

        // ── Multi-start greedy ──────────────────────────────────────────────
        // Try up to MaxRestarts different first tiles; keep the best result.
        int restarts = Math.Min(MaxRestarts, n);
        int[] bestArrangement = Array.Empty<int>();
        float bestConfidence = -1f;

        for (int r = 0; r < restarts; r++)
        {
            ct.ThrowIfCancellationRequested();
            int firstTile = r * (n / restarts); // evenly spread starting tiles
            int[] arrangement = SolveGreedy(horizScore, vertScore, n, cols, rows, firstTile);
            float confidence = ComputeConfidence(arrangement, horizScore, vertScore, n, cols, rows);
            if (confidence > bestConfidence)
            {
                bestConfidence = confidence;
                bestArrangement = arrangement;
            }
            progress?.Report(40 + (r + 1) * 60 / restarts); // 40–100 %
        }

        progress?.Report(100);
        return new SolverResult { Arrangement = bestArrangement, Confidence = bestConfidence };
    }

    /// <summary>
    /// Single greedy pass. Fills positions left-to-right, top-to-bottom.
    /// <paramref name="firstTile"/> is forced into position 0; remaining positions
    /// use the standard best-score heuristic.
    /// </summary>
    private static int[] SolveGreedy(
        float[,] horizScore, float[,] vertScore,
        int n, int cols, int rows, int firstTile)
    {
        int[] arrangement = new int[n];
        bool[] used = new bool[n];

        // Force first tile
        arrangement[0] = firstTile;
        used[firstTile] = true;

        for (int pos = 1; pos < n; pos++)
        {
            int r = pos / cols;
            int c = pos % cols;

            int leftTile = c > 0 ? arrangement[pos - 1] : -1;
            int topTile  = r > 0 ? arrangement[pos - cols] : -1;

            float bestScore = float.MaxValue;
            int bestTile = -1;

            for (int t = 0; t < n; t++)
            {
                if (used[t]) continue;
                float score = 0f;
                if (leftTile >= 0) score += horizScore[leftTile, t];
                if (topTile  >= 0) score += vertScore[topTile, t];
                if (score < bestScore) { bestScore = score; bestTile = t; }
            }

            arrangement[pos] = bestTile;
            used[bestTile] = true;
        }

        return arrangement;
    }

    private static float ComputeConfidence(
        int[] arrangement,
        float[,] horizScore, float[,] vertScore,
        int n, int cols, int rows)
    {
        float totalScore = 0f;
        int boundaryCount = 0;
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

    /// <summary>
    /// Computes average per-pixel RGB MSE along the shared edge.
    /// horizontal=true  → a's right column vs b's left column.
    /// horizontal=false → a's bottom row   vs b's top row.
    /// </summary>
    public static float ComputeEdgeScore(SKBitmap a, SKBitmap b, bool horizontal)
    {
        float sum = 0f;
        if (horizontal)
        {
            int len = Math.Min(a.Height, b.Height);
            int ax = a.Width - 1;
            for (int y = 0; y < len; y++)
                sum += PixelMse(a.GetPixel(ax, y), b.GetPixel(0, y));
            return sum / len;
        }
        else
        {
            int len = Math.Min(a.Width, b.Width);
            int ay = a.Height - 1;
            for (int x = 0; x < len; x++)
                sum += PixelMse(a.GetPixel(x, ay), b.GetPixel(x, 0));
            return sum / len;
        }
    }

    private static float PixelMse(SKColor a, SKColor b)
    {
        float dr = a.Red - b.Red;
        float dg = a.Green - b.Green;
        float db = a.Blue - b.Blue;
        return (dr * dr + dg * dg + db * db) / 3f;
    }
}
