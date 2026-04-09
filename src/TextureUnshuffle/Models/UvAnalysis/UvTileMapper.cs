using System;
using System.Collections.Generic;
using System.Numerics;

namespace TextureUnshuffle.Models.UvAnalysis;

public static class UvTileMapper
{
    /// <summary>
    /// Processes triangle UV edges to build a tile adjacency graph.
    /// </summary>
    /// <param name="triangles">UV triangles from the 3D model.</param>
    /// <param name="cols">Number of tile columns in the texture grid.</param>
    /// <param name="rows">Number of tile rows in the texture grid.</param>
    /// <param name="flipV">
    /// If true (default), inverts V so that V=0 maps to the top row.
    /// Most 3D tools use V=0 at bottom (OpenGL convention), but textures
    /// typically have origin at top-left.
    /// </param>
    public static TileAdjacencyGraph BuildAdjacencyGraph(
        List<UvTriangle> triangles, int cols, int rows, bool flipV = true)
    {
        var graph = new TileAdjacencyGraph(cols * rows);

        foreach (var tri in triangles)
        {
            ProcessEdge(tri.Uv0, tri.Uv1, cols, rows, flipV, graph);
            ProcessEdge(tri.Uv1, tri.Uv2, cols, rows, flipV, graph);
            ProcessEdge(tri.Uv2, tri.Uv0, cols, rows, flipV, graph);
        }

        return graph;
    }

    private static void ProcessEdge(
        Vector2 p1, Vector2 p2,
        int cols, int rows, bool flipV,
        TileAdjacencyGraph graph)
    {
        int col1 = TileCol(p1.X, cols);
        int row1 = TileRow(p1.Y, rows, flipV);
        int col2 = TileCol(p2.X, cols);
        int row2 = TileRow(p2.Y, rows, flipV);

        if (col1 == col2 && row1 == row2) return; // same tile, no constraint

        int tile1 = row1 * cols + col1;
        int tile2 = row2 * cols + col2;

        int dc = col2 - col1;
        int dr = row2 - row1;

        // Only record immediate neighbours (differ by 1 in exactly one axis)
        if (Math.Abs(dc) == 1 && dr == 0)
        {
            if (dc == 1)
                graph.AddRightConstraint(tile1, tile2);
            else
                graph.AddRightConstraint(tile2, tile1);
        }
        else if (Math.Abs(dr) == 1 && dc == 0)
        {
            if (dr == 1)
                graph.AddBelowConstraint(tile1, tile2);
            else
                graph.AddBelowConstraint(tile2, tile1);
        }
        // Diagonal or multi-tile gaps: skip (too ambiguous for hard constraints)
    }

    private static int TileCol(float u, int cols)
        => Math.Clamp((int)(u * cols), 0, cols - 1);

    private static int TileRow(float v, int rows, bool flipV)
        => Math.Clamp((int)((flipV ? 1f - v : v) * rows), 0, rows - 1);
}
