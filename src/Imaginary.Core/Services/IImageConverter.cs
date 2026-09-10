using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public interface IImageConverter
{
    (SKBitmap Bitmap, ImageFormat DetectedFormat) LoadBitmap(Stream stream);
    (SKBitmap Bitmap, ImageFormat DetectedFormat) LoadBitmap(byte[] data);
    (SKBitmap Bitmap, ImageFormat DetectedFormat) LoadBitmap(string filePath);

    byte[] Encode(SKBitmap bitmap, ImageFormat format, int quality = 90);
    void EncodeToStream(SKBitmap bitmap, ImageFormat format, Stream outputStream, int quality = 90);
    SKEncodedImageFormat ToSkEncodedImageFormat(ImageFormat format);
}
