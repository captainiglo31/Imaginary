using Imaginary.Core.Models;
using Imaginary.Core.Services;
using SkiaSharp;

namespace Imaginary.Core.Tests.TestHelpers;

public static class TestImageGenerator
{
    // A valid 1x1 GIF89a image
    public static readonly byte[] MinimalGifBytes = new byte[]
    {
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, // GIF89a
        0x01, 0x00, 0x01, 0x00,             // 1x1
        0x80, 0x00, 0x00,                   // GCT flag, 2 colors
        0xFF, 0xFF, 0xFF,                   // Color 0: White
        0x00, 0x00, 0x00,                   // Color 1: Black
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, // Image descriptor
        0x02, 0x02, 0x44, 0x01, 0x00,       // LZW image data
        0x3B                                // Trailer
    };

    public static byte[] CreatePatternImage(int width, int height, SKEncodedImageFormat format, int quality = 90)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);

            using var paint = new SKPaint();
            var rand = new Random(42);

            for (var i = 0; i < 50; i++)
            {
                paint.Color = new SKColor(
                    (byte)rand.Next(256),
                    (byte)rand.Next(256),
                    (byte)rand.Next(256),
                    (byte)(150 + rand.Next(105)));

                var x = rand.Next(width);
                var y = rand.Next(height);
                var r = rand.Next(10, Math.Max(20, width / 4));
                canvas.DrawCircle(x, y, r, paint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    public static byte[] CreateSolidImage(int width, int height, SKColor color, ImageFormat format, int quality = 90)
    {
        if (format == ImageFormat.Gif)
        {
            return MinimalGifBytes;
        }

        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        var detector = new ImageFormatDetector();
        var converter = new ImageConverter(detector);
        return converter.Encode(bitmap, format, quality);
    }
}
