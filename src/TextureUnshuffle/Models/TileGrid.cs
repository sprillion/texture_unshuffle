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
            int dr = pos / Cols, dc = pos % Cols;
            canvas.DrawBitmap(
                Tiles[tileIdx],
                SKRect.Create(dc * TileSize, dr * TileSize, TileSize, TileSize));
        }
        return result;
    }

    public void SaveAs(string path)
    {
        using var bitmap = BuildResultBitmap();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        System.IO.File.WriteAllBytes(path, data.ToArray());
    }
}
