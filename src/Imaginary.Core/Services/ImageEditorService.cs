using System;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class ImageEditorService : IImageEditorService
{
    public SKBitmap Crop(SKBitmap source, SKRectI cropRect)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Clamp to source bounds
        int left = Math.Clamp(cropRect.Left, 0, source.Width);
        int top = Math.Clamp(cropRect.Top, 0, source.Height);
        int right = Math.Clamp(cropRect.Right, 0, source.Width);
        int bottom = Math.Clamp(cropRect.Bottom, 0, source.Height);

        int width = Math.Max(1, right - left);
        int height = Math.Max(1, bottom - top);
        var clampedRect = new SKRectI(left, top, left + width, top + height);

        var result = new SKBitmap(width, height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(result);
        var srcRect = new SKRect(clampedRect.Left, clampedRect.Top, clampedRect.Right, clampedRect.Bottom);
        var destRect = new SKRect(0, 0, width, height);
        canvas.DrawBitmap(source, srcRect, destRect);
        canvas.Flush();

        return result;
    }

    public SKBitmap ApplyPixelate(SKBitmap source, SKRectI targetRect, int pixelSize = 16)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = source.Copy();
        int left = Math.Clamp(targetRect.Left, 0, result.Width);
        int top = Math.Clamp(targetRect.Top, 0, result.Height);
        int right = Math.Clamp(targetRect.Right, 0, result.Width);
        int bottom = Math.Clamp(targetRect.Bottom, 0, result.Height);

        if (left >= right || top >= bottom) return result;

        pixelSize = Math.Max(2, pixelSize);
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };

        for (int y = top; y < bottom; y += pixelSize)
        {
            int blockH = Math.Min(pixelSize, bottom - y);
            for (int x = left; x < right; x += pixelSize)
            {
                int blockW = Math.Min(pixelSize, right - x);

                // Sample center of block
                int sampleX = Math.Min(x + blockW / 2, result.Width - 1);
                int sampleY = Math.Min(y + blockH / 2, result.Height - 1);
                paint.Color = result.GetPixel(sampleX, sampleY);

                canvas.DrawRect(x, y, blockW, blockH, paint);
            }
        }

        canvas.Flush();
        return result;
    }

    public SKBitmap ApplyGaussianBlur(SKBitmap source, SKRectI targetRect, float sigma = 12f)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = source.Copy();
        int left = Math.Clamp(targetRect.Left, 0, result.Width);
        int top = Math.Clamp(targetRect.Top, 0, result.Height);
        int right = Math.Clamp(targetRect.Right, 0, result.Width);
        int bottom = Math.Clamp(targetRect.Bottom, 0, result.Height);

        int width = right - left;
        int height = bottom - top;
        if (width <= 0 || height <= 0) return result;

        // Crop the subregion
        using var subBitmap = Crop(source, new SKRectI(left, top, right, bottom));

        // Create blurred copy
        using var blurPaint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateBlur(sigma, sigma),
            IsAntialias = true
        };

        using var canvas = new SKCanvas(result);
        canvas.Save();
        canvas.ClipRect(new SKRect(left, top, right, bottom));
        canvas.DrawBitmap(subBitmap, left, top, blurPaint);
        canvas.Restore();
        canvas.Flush();

        return result;
    }

    public SKBitmap ApplyBlackout(SKBitmap source, SKRectI targetRect, SKColor color)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = source.Copy();
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        canvas.DrawRect(new SKRect(targetRect.Left, targetRect.Top, targetRect.Right, targetRect.Bottom), paint);
        canvas.Flush();
        return result;
    }

    public SKBitmap RemoveBackgroundByColor(SKBitmap source, SKColor keyColor, float tolerance = 0.15f)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Ensure RGBA with alpha channel
        var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();

        float maxDistance = MathF.Sqrt(255f * 255f * 3f);
        float threshold = tolerance * maxDistance;

        for (int y = 0; y < result.Height; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                var pixel = result.GetPixel(x, y);

                float dr = pixel.Red - keyColor.Red;
                float dg = pixel.Green - keyColor.Green;
                float db = pixel.Blue - keyColor.Blue;
                float distance = MathF.Sqrt(dr * dr + dg * dg + db * db);

                if (distance <= threshold)
                {
                    result.SetPixel(x, y, SKColors.Transparent);
                }
                else if (distance < threshold * 1.25f)
                {
                    // Soft edge blend
                    float alphaFactor = (distance - threshold) / (threshold * 0.25f);
                    byte newAlpha = (byte)(pixel.Alpha * alphaFactor);
                    result.SetPixel(x, y, new SKColor(pixel.Red, pixel.Green, pixel.Blue, newAlpha));
                }
            }
        }

        return result;
    }

    public SKBitmap DrawArrow(SKBitmap source, SKPoint start, SKPoint end, SKColor color, float strokeWidth = 4f)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = source.Copy();
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = strokeWidth,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        // Main line
        canvas.DrawLine(start, end, paint);

        // Arrow head
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float angle = MathF.Atan2(dy, dx);
        float arrowLen = Math.Max(16f, strokeWidth * 4f);
        float wingAngle = 28f * (MathF.PI / 180f);

        var wing1 = new SKPoint(
            end.X - arrowLen * MathF.Cos(angle - wingAngle),
            end.Y - arrowLen * MathF.Sin(angle - wingAngle));

        var wing2 = new SKPoint(
            end.X - arrowLen * MathF.Cos(angle + wingAngle),
            end.Y - arrowLen * MathF.Sin(angle + wingAngle));

        using var headPaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var path = new SKPath();
        path.MoveTo(end);
        path.LineTo(wing1);
        path.LineTo(wing2);
        path.Close();

        canvas.DrawPath(path, headPaint);
        canvas.Flush();

        return result;
    }

    public SKBitmap DrawRectangle(SKBitmap source, SKRect rect, SKColor color, float strokeWidth = 3f, bool isFilled = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = source.Copy();
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = strokeWidth,
            Style = isFilled ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
            IsAntialias = true
        };

        canvas.DrawRoundRect(rect, 4f, 4f, paint);
        canvas.Flush();
        return result;
    }

    public SKBitmap DrawOval(SKBitmap source, SKRect rect, SKColor color, float strokeWidth = 3f, bool isFilled = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = source.Copy();
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = strokeWidth,
            Style = isFilled ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
            IsAntialias = true
        };

        canvas.DrawOval(rect, paint);
        canvas.Flush();
        return result;
    }

    public SKBitmap DrawStepBadge(SKBitmap source, SKPoint center, int number, SKColor badgeColor, float radius = 18f)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = source.Copy();
        using var canvas = new SKCanvas(result);

        // Outer Glow/Shadow
        using var shadowPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 100),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(center.X, center.Y + 1.5f, radius + 1f, shadowPaint);

        // Badge Circle
        using var circlePaint = new SKPaint
        {
            Color = badgeColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(center, radius, circlePaint);

        // White border
        using var borderPaint = new SKPaint
        {
            Color = SKColors.White,
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawCircle(center, radius, borderPaint);

        // Step number text
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = radius * 1.15f,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };

        // Vertically center text
        var textBounds = new SKRect();
        string numStr = number.ToString();
        textPaint.MeasureText(numStr, ref textBounds);
        float textY = center.Y - textBounds.MidY;

        canvas.DrawText(numStr, center.X, textY, textPaint);
        canvas.Flush();

        return result;
    }

    public SKBitmap DrawHighlighter(SKBitmap source, SKPoint start, SKPoint end, SKColor color, float strokeWidth = 24f)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = source.Copy();
        using var canvas = new SKCanvas(result);

        // 45% transparency for highlighter
        var highlightColor = new SKColor(color.Red, color.Green, color.Blue, 115);

        using var paint = new SKPaint
        {
            Color = highlightColor,
            StrokeWidth = strokeWidth,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            BlendMode = SKBlendMode.SrcOver,
            IsAntialias = true
        };

        canvas.DrawLine(start, end, paint);
        canvas.Flush();
        return result;
    }

    public SKBitmap DrawText(SKBitmap source, string text, SKPoint position, SKColor textColor, float fontSize = 20f, SKColor? backgroundColor = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrEmpty(text)) return source.Copy();

        var result = source.Copy();
        using var canvas = new SKCanvas(result);

        using var textPaint = new SKPaint
        {
            Color = textColor,
            TextSize = fontSize,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };

        var bounds = new SKRect();
        textPaint.MeasureText(text, ref bounds);

        if (backgroundColor.HasValue)
        {
            using var bgPaint = new SKPaint
            {
                Color = backgroundColor.Value,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            var bgRect = new SKRect(
                position.X - 6f,
                position.Y - bounds.Height - 6f,
                position.X + bounds.Width + 6f,
                position.Y + 6f);

            canvas.DrawRoundRect(bgRect, 4f, 4f, bgPaint);
        }

        canvas.DrawText(text, position.X, position.Y, textPaint);
        canvas.Flush();

        return result;
    }
}
