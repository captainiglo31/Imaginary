using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public interface IAutoRedactionService
{
    IReadOnlyList<DetectedSensitiveRegion> DetectRegions(
        SKBitmap bitmap,
        bool detectFaces = true,
        bool detectPlates = true);

    SKBitmap RedactRegions(
        SKBitmap bitmap,
        IEnumerable<DetectedSensitiveRegion> regions,
        RedactionStyle style = RedactionStyle.Pixelate,
        int intensity = 16);

    SKBitmap AutoRedact(
        SKBitmap bitmap,
        bool detectFaces = true,
        bool detectPlates = true,
        RedactionStyle style = RedactionStyle.Pixelate,
        int intensity = 16);
}
