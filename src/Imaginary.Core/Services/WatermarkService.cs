using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class WatermarkService : IWatermarkService
{
    public void ApplyWatermark(SKBitmap bitmap, WatermarkOptions? options)
    {
        if (bitmap == null || options == null || options.Type == WatermarkType.None)
        {
            return;
        }

        using var canvas = new SKCanvas(bitmap);

        if (options.Type == WatermarkType.Text && !string.IsNullOrWhiteSpace(options.Text))
        {
            ApplyTextWatermark(canvas, bitmap.Width, bitmap.Height, options);
        }
        else if (options.Type == WatermarkType.Image)
        {
            ApplyImageWatermark(canvas, bitmap.Width, bitmap.Height, options);
        }
    }

    private static void ApplyTextWatermark(SKCanvas canvas, int width, int height, WatermarkOptions options)
    {
        var textColor = SKColor.TryParse(options.ColorHex, out var parsed) ? parsed : SKColors.White;
        var alpha = (byte)(Math.Clamp(options.Opacity, 0.05f, 1.0f) * 255);

        // Adjust font size proportionally if image is small/large
        var fontSize = Math.Max(12f, options.FontSize);
        if (fontSize > height / 4f) fontSize = height / 4f;

        using var typeface = SKTypeface.FromFamilyName(options.FontFamily, SKFontStyle.Bold);
        using var paint = new SKPaint
        {
            Typeface = typeface,
            TextSize = fontSize,
            IsAntialias = true,
            Color = textColor.WithAlpha(alpha)
        };

        var textBounds = new SKRect();
        paint.MeasureText(options.Text, ref textBounds);
        var textWidth = textBounds.Width;
        var textHeight = textBounds.Height;

        var (x, y) = CalculateCoordinates(
            width,
            height,
            textWidth,
            textHeight,
            options.Position,
            options.MarginPx);

        // Draw shadow for contrast
        using var shadowPaint = new SKPaint
        {
            Typeface = typeface,
            TextSize = fontSize,
            IsAntialias = true,
            Color = SKColors.Black.WithAlpha((byte)(alpha * 0.6f))
        };
        canvas.DrawText(options.Text, x + 2, y + 2, shadowPaint);

        // Draw actual text
        canvas.DrawText(options.Text, x, y, paint);
    }

    private static void ApplyImageWatermark(SKCanvas canvas, int width, int height, WatermarkOptions options)
    {
        SKBitmap? logo = null;
        try
        {
            if (options.ImageData != null && options.ImageData.Length > 0)
            {
                logo = SKBitmap.Decode(options.ImageData);
            }
            else if (!string.IsNullOrWhiteSpace(options.ImagePath) && File.Exists(options.ImagePath))
            {
                logo = SKBitmap.Decode(options.ImagePath);
            }

            if (logo == null) return;

            // Target scale
            var scaleFraction = Math.Clamp(options.ImageScalePercent / 100f, 0.05f, 0.9f);
            var targetLogoWidth = (int)(width * scaleFraction);
            var targetLogoHeight = (int)((float)targetLogoWidth / logo.Width * logo.Height);

            if (targetLogoWidth <= 0 || targetLogoHeight <= 0) return;

            var (x, y) = CalculateCoordinates(
                width,
                height,
                targetLogoWidth,
                targetLogoHeight,
                options.Position,
                options.MarginPx,
                isBaseline: false);

            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)(Math.Clamp(options.Opacity, 0.05f, 1.0f) * 255)),
                FilterQuality = SKFilterQuality.High
            };

            var destRect = new SKRect(x, y, x + targetLogoWidth, y + targetLogoHeight);
            canvas.DrawBitmap(logo, destRect, paint);
        }
        finally
        {
            logo?.Dispose();
        }
    }

    private static (float X, float Y) CalculateCoordinates(
        int containerW,
        int containerH,
        float itemW,
        float itemH,
        WatermarkPosition pos,
        int margin,
        bool isBaseline = true)
    {
        float x;
        float y;

        switch (pos)
        {
            case WatermarkPosition.TopLeft:
                x = margin;
                y = isBaseline ? margin + itemH : margin;
                break;

            case WatermarkPosition.TopCenter:
                x = (containerW - itemW) / 2f;
                y = isBaseline ? margin + itemH : margin;
                break;

            case WatermarkPosition.TopRight:
                x = containerW - itemW - margin;
                y = isBaseline ? margin + itemH : margin;
                break;

            case WatermarkPosition.Center:
                x = (containerW - itemW) / 2f;
                y = isBaseline ? (containerH + itemH) / 2f : (containerH - itemH) / 2f;
                break;

            case WatermarkPosition.BottomLeft:
                x = margin;
                y = containerH - margin;
                break;

            case WatermarkPosition.BottomCenter:
                x = (containerW - itemW) / 2f;
                y = containerH - margin;
                break;

            case WatermarkPosition.BottomRight:
            default:
                x = containerW - itemW - margin;
                y = containerH - margin;
                break;
        }

        return (x, y);
    }
}
