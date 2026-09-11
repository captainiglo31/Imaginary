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

    public SKBitmap ApplyBlurBrush(SKBitmap source, IEnumerable<SKPoint> strokePoints, float brushRadius = 24f, float sigma = 10f)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(strokePoints);

        var pts = strokePoints.ToList();
        if (pts.Count == 0) return source.Copy();

        var result = source.Copy();

        // Calculate bounding box of the brush stroke
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in pts)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        float pad = brushRadius + sigma * 2f + 4f;
        int left = Math.Clamp((int)MathF.Floor(minX - pad), 0, result.Width);
        int top = Math.Clamp((int)MathF.Floor(minY - pad), 0, result.Height);
        int right = Math.Clamp((int)MathF.Ceiling(maxX + pad), 0, result.Width);
        int bottom = Math.Clamp((int)MathF.Ceiling(maxY + pad), 0, result.Height);

        int subW = right - left;
        int subH = bottom - top;
        if (subW <= 0 || subH <= 0) return result;

        // Crop the subregion to blur
        using var subBitmap = Crop(source, new SKRectI(left, top, right, bottom));

        // Create blur filter paint
        using var blurPaint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateBlur(sigma, sigma),
            IsAntialias = true
        };

        // Create clip path for the brush stroke
        using var clipPath = new SKPath();
        if (pts.Count == 1)
        {
            clipPath.AddCircle(pts[0].X, pts[0].Y, brushRadius);
        }
        else
        {
            using var rawPath = new SKPath();
            rawPath.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++)
            {
                rawPath.LineTo(pts[i]);
            }

            using var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = brushRadius * 2f,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };
            strokePaint.GetFillPath(rawPath, clipPath);
        }

        using var canvas = new SKCanvas(result);
        canvas.Save();
        canvas.ClipPath(clipPath, antialias: true);
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

        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1f) return result;

        float uX = dx / len;
        float uY = dy / len;
        float arrowLen = Math.Max(16f, strokeWidth * 4f);
        float arrowWidth = arrowLen * 0.55f;

        // Base center where the shaft connects to the arrow head (shaft does NOT poke into tip)
        float shaftCutoff = Math.Min(arrowLen * 0.85f, len * 0.8f);
        var baseCenter = new SKPoint(end.X - uX * shaftCutoff, end.Y - uY * shaftCutoff);

        // Perpendicular vector for arrowhead wings
        float perpX = -uY;
        float perpY = uX;
        var wing1 = new SKPoint(baseCenter.X + perpX * arrowWidth, baseCenter.Y + perpY * arrowWidth);
        var wing2 = new SKPoint(baseCenter.X - perpX * arrowWidth, baseCenter.Y - perpY * arrowWidth);

        // Draw shaft line up to baseCenter
        using var shaftPaint = new SKPaint
        {
            Color = color,
            StrokeWidth = strokeWidth,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        canvas.DrawLine(start, baseCenter, shaftPaint);

        // Draw sharp arrowhead polygon
        using var headPath = new SKPath();
        headPath.MoveTo(end);
        headPath.LineTo(wing1);
        headPath.LineTo(wing2);
        headPath.Close();

        using var headPaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawPath(headPath, headPaint);

        // Sharp stroke around arrowhead for crisp edges
        using var headStroke = new SKPaint
        {
            Color = color,
            StrokeWidth = Math.Max(1f, strokeWidth * 0.4f),
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Miter,
            IsAntialias = true
        };
        canvas.DrawPath(headPath, headStroke);

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

        // Determine high-contrast text and border color based on relative luminance (ITU-R BT.709)
        float lum = (0.2126f * badgeColor.Red + 0.7152f * badgeColor.Green + 0.0722f * badgeColor.Blue) / 255f;
        bool isLight = lum > 0.55f;

        SKColor textColor = isLight ? new SKColor(15, 23, 42) : SKColors.White;
        SKColor borderColor = isLight ? new SKColor(15, 23, 42, 160) : SKColors.White;

        // High-contrast border
        using var borderPaint = new SKPaint
        {
            Color = borderColor,
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawCircle(center, radius, borderPaint);

        // Step number text
        using var textPaint = new SKPaint
        {
            Color = textColor,
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
