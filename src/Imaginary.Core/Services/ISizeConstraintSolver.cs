using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public record SizeConstraintResult(
    byte[] EncodedData,
    ImageDimensions FinalDimensions,
    string AppliedStrategy,
    List<string> Warnings);

public interface ISizeConstraintSolver
{
    SizeConstraintResult Solve(SKBitmap bitmap, ImageFormat targetFormat, ConversionOptions options);
}
