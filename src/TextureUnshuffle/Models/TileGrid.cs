using SkiaSharp;
using System;
using System.Linq;

namespace TextureUnshuffle.Models;

public class TileGrid
{
    public SKBitmap OriginalBitmap { get; private set; } = null!;
    public int TileSize { get; private set; }
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public SKBitmap[] Tiles { get; private set; } = Array.Empty<SKBitmap>();

    /// <summary>arrangement[displayPos] = tileIndex</summary>
    public int[] Arrangement { get; set; } = Array.Empty<int>();

    /// <summary>rotations[displayPos] = 90° CW turns (0–3) for the tile at that position</summary>
    public int[] Rotations { get; set; } = Array.Empty<int>();

    /// <summary>flips[displayPos] — bitmask: bit 0 = flip H, bit 1 = flip V (applied after rotation)</summary>
    public int[] Flips { get; set; } = Array.Empty<int>();

    public string FilePath { get; private set; } = string.Empty;

    private TileGrid() { }

    public static TileGrid Load(string path, int tileSize, int cols, int rows)
    {
        var bitmap = SKBitmap.Decode(path)
            ?? throw new Exception($"Не удалось декодировать изображение: {path}");

        if (cols * tileSize > bitmap.Width || rows * tileSize > bitmap.Height)
            throw new Exception(
                $"Параметры сетки ({cols}×{rows} × {tileSize}px = {cols * tileSize}×{rows * tileSize}px) " +
                $"выходят за размер изображения {bitmap.Width}×{bitmap.Height}px");

        var tiles = new SKBitmap[cols * rows];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var tile = new SKBitmap(tileSize, tileSize);
                using var canvas = new SKCanvas(tile);
                canvas.DrawBitmap(
                    bitmap,
                    SKRect.Create(c * tileSize, r * tileSize, tileSize, tileSize),
                    SKRect.Create(0, 0, tileSize, tileSize));
                tiles[r * cols + c] = tile;
            }
        }

        return new TileGrid
        {
            OriginalBitmap = bitmap,
            TileSize = tileSize,
            Cols = cols,
            Rows = rows,
            Tiles = tiles,
            Arrangement = Enumerable.Range(0, cols * rows).ToArray(),
            Rotations   = new int[cols * rows],
            Flips       = new int[cols * rows],
            FilePath = path
        };
    }

    public SKBitmap BuildResultBitmap()
    {
        var result = new SKBitmap(Cols * TileSize, Rows * TileSize);
        using var canvas = new SKCanvas(result);
        for (int pos = 0; pos < Arrangement.Length; pos++)
        {
            int tileIdx = Arrangement[pos];
            int rot  = Rotations.Length > pos ? Rotations[pos] : 0;
            int flip = Flips.Length    > pos ? Flips[pos]    : 0;
            int dr = pos / Cols, dc = pos % Cols;
            var dest = SKRect.Create(dc * TileSize, dr * TileSize, TileSize, TileSize);

            SKBitmap? rotated = null;
            SKBitmap? flipped = null;
            try
            {
                SKBitmap current = Tiles[tileIdx];
                if (rot != 0)  { rotated = RotateBitmap(current, rot); current = rotated; }
                if (flip != 0) { flipped = FlipBitmap(current, flip);  current = flipped; }
                canvas.DrawBitmap(current, dest);
            }
            finally
            {
                rotated?.Dispose();
                flipped?.Dispose();
            }
        }
        return result;
    }

    /// <summary>
    /// Returns a new SKBitmap rotated by <paramref name="rotation"/> × 90° clockwise.
    /// rotation=0 → copy, 1 → 90° CW, 2 → 180°, 3 → 270° CW.
    /// Caller is responsible for disposing the returned bitmap.
    /// </summary>
    public static SKBitmap RotateBitmap(SKBitmap src, int rotation)
    {
        rotation = ((rotation % 4) + 4) % 4;
        if (rotation == 0)
        {
            var copy = new SKBitmap(src.Width, src.Height);
            using var c = new SKCanvas(copy);
            c.DrawBitmap(src, 0, 0);
            return copy;
        }
        bool swap = rotation == 1 || rotation == 3;
        int dstW = swap ? src.Height : src.Width;
        int dstH = swap ? src.Width  : src.Height;
        var dst = new SKBitmap(dstW, dstH);
        using var canvas = new SKCanvas(dst);
        canvas.Translate(dstW / 2f, dstH / 2f);
        canvas.RotateDegrees(rotation * 90f);
        canvas.Translate(-src.Width / 2f, -src.Height / 2f);
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }

    /// <summary>
    /// Returns a new SKBitmap flipped according to the bitmask:
    /// bit 0 = flip H (mirror left-right), bit 1 = flip V (mirror top-bottom).
    /// Caller is responsible for disposing the returned bitmap.
    /// </summary>
    public static SKBitmap FlipBitmap(SKBitmap src, int flip)
    {
        var dst = new SKBitmap(src.Width, src.Height);
        using var canvas = new SKCanvas(dst);
        float scaleX = (flip & 1) != 0 ? -1f : 1f;
        float scaleY = (flip & 2) != 0 ? -1f : 1f;
        canvas.Scale(scaleX, scaleY, src.Width / 2f, src.Height / 2f);
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }

    public void SaveAs(string path)
    {
        using var bitmap = BuildResultBitmap();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        System.IO.File.WriteAllBytes(path, data.ToArray());
    }
}
