using SkiaSharp;

namespace Imaginary.Core.Services;

public interface IImageEditorService
{
    /// <summary>
    /// Cuts the specified subregion from the bitmap.
    /// </summary>
    SKBitmap Crop(SKBitmap source, SKRectI cropRect);

    /// <summary>
    /// Applies a pixelation mosaic effect to the specified region (ideal for GDPR blurring of faces, licenses, names).
    /// </summary>
    SKBitmap ApplyPixelate(SKBitmap source, SKRectI targetRect, int pixelSize = 16);

    /// <summary>
    /// Applies a smooth Gaussian blur to the specified region.
    /// </summary>
    SKBitmap ApplyGaussianBlur(SKBitmap source, SKRectI targetRect, float sigma = 12f);

    /// <summary>
    /// Applies a smooth Gaussian blur brush along the specified stroke points.
    /// </summary>
    SKBitmap ApplyBlurBrush(SKBitmap source, IEnumerable<SKPoint> strokePoints, float brushRadius = 24f, float sigma = 10f);

    /// <summary>
    /// Draws a solid blackout rectangle over sensitive information (IBAN, passwords, tokens).
    /// </summary>
    SKBitmap ApplyBlackout(SKBitmap source, SKRectI targetRect, SKColor color);

    /// <summary>
    /// Removes the background by color comparison with a threshold tolerance (Magic Wand).
    /// </summary>
    SKBitmap RemoveBackgroundByColor(SKBitmap source, SKColor keyColor, float tolerance = 0.15f);

    /// <summary>
    /// Draws an arrow pointing from start to end with an arrowhead.
    /// </summary>
    SKBitmap DrawArrow(SKBitmap source, SKPoint start, SKPoint end, SKColor color, float strokeWidth = 4f);

    /// <summary>
    /// Draws a rectangle outline or filled rectangle.
    /// </summary>
    SKBitmap DrawRectangle(SKBitmap source, SKRect rect, SKColor color, float strokeWidth = 3f, bool isFilled = false);

    /// <summary>
    /// Draws an oval/circle outline or filled oval.
    /// </summary>
    SKBitmap DrawOval(SKBitmap source, SKRect rect, SKColor color, float strokeWidth = 3f, bool isFilled = false);

    /// <summary>
    /// Draws a numbered circular step badge (e.g. 1, 2, 3) for software walkthroughs and documentation.
    /// </summary>
    SKBitmap DrawStepBadge(SKBitmap source, SKPoint center, int number, SKColor badgeColor, float radius = 18f);

    /// <summary>
    /// Draws a semi-transparent highlighter stroke over text or regions.
    /// </summary>
    SKBitmap DrawHighlighter(SKBitmap source, SKPoint start, SKPoint end, SKColor color, float strokeWidth = 24f);

    /// <summary>
    /// Draws text with optional background container box.
    /// </summary>
    SKBitmap DrawText(SKBitmap source, string text, SKPoint position, SKColor textColor, float fontSize = 20f, SKColor? backgroundColor = null);
}
