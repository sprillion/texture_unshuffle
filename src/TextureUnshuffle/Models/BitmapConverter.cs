using SkiaSharp;
using System.IO;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace TextureUnshuffle.Models;

public static class BitmapConverter
{
    public static AvaloniaBitmap ToAvaloniaBitmap(SKBitmap skBitmap)
    {
        using var image = SKImage.FromBitmap(skBitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var stream = new MemoryStream();
        encoded.SaveTo(stream);
        stream.Position = 0;
        return new AvaloniaBitmap(stream);
    }
}
