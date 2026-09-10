using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public interface IWatermarkService
{
    void ApplyWatermark(SKBitmap bitmap, WatermarkOptions? options);
}
