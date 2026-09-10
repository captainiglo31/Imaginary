using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public interface IImageResizer
{
    ImageDimensions CalculateNewDimensions(ImageDimensions original, ConversionOptions options);
    SKBitmap Resize(SKBitmap source, ConversionOptions options);
    SKBitmap Resize(SKBitmap source, int targetWidth, int targetHeight);
    SKBitmap ResizeByScale(SKBitmap source, double scale);
}
