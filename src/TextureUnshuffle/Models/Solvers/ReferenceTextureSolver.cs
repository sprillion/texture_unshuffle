using SkiaSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using TextureUnshuffle.Models;

namespace TextureUnshuffle.Models.Solvers;

/// <summary>
/// Matches source tiles (high-res, shuffled + rotated) against reference tiles
/// (low-res, correct position, correct rotation) using downscaled MSE comparison
/// across all 4 rotations. Uses best-bid greedy assignment.
/// </summary>
public class ReferenceTextureSolver
{
    /// <summary>
    /// Solve the arrangement.
    /// <paramref name="srcTiles"/> — tiles from the high-res shuffled+rotated texture.
    /// <paramref name="refTiles"/> — tiles from the low-res reference texture (correct order, no rotation).
    /// Both arrays must have length cols × rows.
    /// Progress: 0–80 = cost matrix, 80–100 = assignment.
    /// </summary>
    public Task<SolverResult> SolveAsync(
        SKBitmap[] srcTiles,
        SKBitmap[] refTiles,
        int cols, int rows,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
        => Task.Run(() => Solve(srcTiles, refTiles, cols, rows, progress, ct), ct);

    private static SolverResult Solve(
        SKBitmap[] srcTiles,
        SKBitmap[] refTiles,
        int cols, int rows,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        int n           = cols * rows;
        int refTileSize = refTiles[0].Width;

        // Precompute all (srcTile × 4 rotations) downscaled to refTileSize.
        // This avoids redundant rotate+downscale inside the O(n²) cost loop.
        var srcCached = new SKBitmap[n, 4];
        try
        {
            for (int si = 0; si < n; si++)
            {
                ct.ThrowIfCancellationRequested();
                for (int rot = 0; rot < 4; rot++)
                {
                    using var rotated = TileGrid.RotateBitmap(srcTiles[si], rot);
                    srcCached[si, rot] = Downscale(rotated, refTileSize);
                }
            }

            // Build cost matrix: cost[refPos, srcIdx] = min MSE over 4 rotations.
            float[,] cost    = new float[n, n];
            int[,]   bestRot = new int[n, n];

            for (int refPos = 0; refPos < n; refPos++)
            {
                ct.ThrowIfCancellationRequested();
                var refTile = refTiles[refPos];

                for (int si = 0; si < n; si++)
                {
                    float minMse = float.MaxValue;
                    int   minRot = 0;
                    for (int rot = 0; rot < 4; rot++)
                    {
                        float mse = ComputeMse(refTile, srcCached[si, rot]);
                        if (mse < minMse) { minMse = mse; minRot = rot; }
                    }
                    cost[refPos, si]    = minMse;
                    bestRot[refPos, si] = minRot;
                }

                progress?.Report(refPos * 80 / n); // 0–80 %
            }

            // Best-bid greedy assignment:
            // At each step pick the globally minimum cost cell among unassigned pairs.
            int[] arrangement = new int[n];
            int[] rotations   = new int[n];
            bool[] usedRef    = new bool[n];
            bool[] usedSrc    = new bool[n];

            for (int step = 0; step < n; step++)
            {
                ct.ThrowIfCancellationRequested();

                float minCost = float.MaxValue;
                int   bestRef = -1;
                int   bestSrc = -1;

                for (int rp = 0; rp < n; rp++)
                {
                    if (usedRef[rp]) continue;
                    for (int si = 0; si < n; si++)
                    {
                        if (usedSrc[si]) continue;
                        if (cost[rp, si] < minCost)
                        {
                            minCost = cost[rp, si];
                            bestRef = rp;
                            bestSrc = si;
                        }
                    }
                }

                arrangement[bestRef] = bestSrc;
                rotations[bestRef]   = bestRot[bestRef, bestSrc];
                usedRef[bestRef]     = true;
                usedSrc[bestSrc]     = true;

                progress?.Report(80 + (step + 1) * 20 / n); // 80–100 %
            }

            progress?.Report(100);
            return new SolverResult
            {
                Arrangement = arrangement,
                Rotations   = rotations,
                Confidence  = 1f
            };
        }
        finally
        {
            // Dispose all cached bitmaps
            for (int si = 0; si < n; si++)
                for (int rot = 0; rot < 4; rot++)
                    srcCached[si, rot]?.Dispose();
        }
    }

    /// <summary>
    /// Downscales <paramref name="src"/> to <paramref name="targetSize"/> × <paramref name="targetSize"/>
    /// using high-quality filtering. Caller must dispose the result.
    /// </summary>
    private static SKBitmap Downscale(SKBitmap src, int targetSize)
    {
        var dst = new SKBitmap(targetSize, targetSize);
        using var canvas = new SKCanvas(dst);
        using var paint  = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        canvas.DrawBitmap(src,
            SKRect.Create(0, 0, src.Width,  src.Height),
            SKRect.Create(0, 0, targetSize, targetSize),
            paint);
        return dst;
    }

    /// <summary>Average per-pixel RGB MSE between two bitmaps (must be same size).</summary>
    private static float ComputeMse(SKBitmap a, SKBitmap b)
    {
        int w = Math.Min(a.Width, b.Width);
        int h = Math.Min(a.Height, b.Height);
        float sum = 0f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var ca = a.GetPixel(x, y);
                var cb = b.GetPixel(x, y);
                float dr = ca.Red   - cb.Red;
                float dg = ca.Green - cb.Green;
                float db = ca.Blue  - cb.Blue;
                sum += (dr * dr + dg * dg + db * db) / 3f;
            }
        }
        return sum / (w * h);
    }
}
