using SkiaSharp;

namespace Imaginary.Core.Services;

public interface IColorQuantizer
{
    SKBitmap Quantize(SKBitmap source, int maxColors = 256);
}
