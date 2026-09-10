using SkiaSharp;

namespace Imaginary.Core.Services;

public interface IIcoEncoder
{
    byte[] EncodeToIco(SKBitmap sourceBitmap, int[]? customSizes = null);
    void SaveToIco(SKBitmap sourceBitmap, Stream outputStream, int[]? customSizes = null);
}
