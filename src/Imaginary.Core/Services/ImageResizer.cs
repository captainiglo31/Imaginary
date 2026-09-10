using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class ImageResizer : IImageResizer
{
    public ImageDimensions CalculateNewDimensions(ImageDimensions original, ConversionOptions options)
    {
        if (original.Width <= 0 || original.Height <= 0)
        {
            return original;
        }

        switch (options.ResizeMode)
        {
            case ResizeMode.None:
                return original;

            case ResizeMode.Percentage:
            {
                var factor = options.ResizePercentage / 100.0;
                var w = Math.Max(1, (int)Math.Round(original.Width * factor));
                var h = Math.Max(1, (int)Math.Round(original.Height * factor));
                return new ImageDimensions(w, h);
            }

            case ResizeMode.MaxEdge:
            {
                var maxEdge = options.MaxEdgeLength ?? options.TargetWidth ?? options.TargetHeight ?? 1920;
                if (maxEdge <= 0) return original;

                if (original.Width >= original.Height)
                {
                    var w = maxEdge;
                    var h = Math.Max(1, (int)Math.Round((double)maxEdge / original.Width * original.Height));
                    return new ImageDimensions(w, h);
                }
                else
                {
                    var h = maxEdge;
                    var w = Math.Max(1, (int)Math.Round((double)maxEdge / original.Height * original.Width));
                    return new ImageDimensions(w, h);
                }
            }

            case ResizeMode.FillCrop:
            case ResizeMode.Pad:
            {
                var targetW = options.TargetWidth ?? original.Width;
                var targetH = options.TargetHeight ?? original.Height;
                return new ImageDimensions(targetW, targetH);
            }

            case ResizeMode.Fit:
            case ResizeMode.AbsolutePixels:
            {
                var hasWidth = options.TargetWidth.HasValue && options.TargetWidth.Value > 0;
                var hasHeight = options.TargetHeight.HasValue && options.TargetHeight.Value > 0;

                if (!hasWidth && !hasHeight)
                {
                    return original;
                }

                if (!options.MaintainAspectRatio && options.ResizeMode != ResizeMode.Fit)
                {
                    var w = hasWidth ? options.TargetWidth!.Value : original.Width;
                    var h = hasHeight ? options.TargetHeight!.Value : original.Height;
                    return new ImageDimensions(w, h);
                }

                if (hasWidth && !hasHeight)
                {
                    var targetW = options.TargetWidth!.Value;
                    var targetH = Math.Max(1, (int)Math.Round((double)targetW / original.Width * original.Height));
                    return new ImageDimensions(targetW, targetH);
                }

                if (!hasWidth && hasHeight)
                {
                    var targetH = options.TargetHeight!.Value;
                    var targetW = Math.Max(1, (int)Math.Round((double)targetH / original.Height * original.Width));
                    return new ImageDimensions(targetW, targetH);
                }

                // Both specified: fit within bounding box
                var ratioX = (double)options.TargetWidth!.Value / original.Width;
                var ratioY = (double)options.TargetHeight!.Value / original.Height;
                var ratio = Math.Min(ratioX, ratioY);

                var fitW = Math.Max(1, (int)Math.Round(original.Width * ratio));
                var fitH = Math.Max(1, (int)Math.Round(original.Height * ratio));
                return new ImageDimensions(fitW, fitH);
            }

            default:
                return original;
        }
    }

    public SKBitmap Resize(SKBitmap source, ConversionOptions options)
    {
        if (options.ResizeMode == ResizeMode.FillCrop)
        {
            return ResizeFillCrop(source, options.TargetWidth ?? source.Width, options.TargetHeight ?? source.Height);
        }

        if (options.ResizeMode == ResizeMode.Pad)
        {
            return ResizePad(source, options.TargetWidth ?? source.Width, options.TargetHeight ?? source.Height, options.PadColor);
        }

        var originalDims = new ImageDimensions(source.Width, source.Height);
        var newDims = CalculateNewDimensions(originalDims, options);

        if (newDims.Width == source.Width && newDims.Height == source.Height)
        {
            return source.Copy();
        }

        return Resize(source, newDims.Width, newDims.Height);
    }

    public SKBitmap Resize(SKBitmap source, int targetWidth, int targetHeight)
    {
        targetWidth = Math.Max(1, targetWidth);
        targetHeight = Math.Max(1, targetHeight);

        var info = new SKImageInfo(targetWidth, targetHeight, source.ColorType, source.AlphaType);
        var resized = source.Resize(info, SKFilterQuality.High);
        if (resized != null)
        {
            return resized;
        }

        // Fallback
        var fallback = new SKBitmap(info);
        using var canvas = new SKCanvas(fallback);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKRect(0, 0, targetWidth, targetHeight), paint);
        return fallback;
    }

    public SKBitmap ResizeByScale(SKBitmap source, double scale)
    {
        scale = Math.Clamp(scale, 0.01, 1.0);
        var targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        return Resize(source, targetWidth, targetHeight);
    }

    private SKBitmap ResizeFillCrop(SKBitmap source, int targetW, int targetH)
    {
        targetW = Math.Max(1, targetW);
        targetH = Math.Max(1, targetH);

        // Scale factor: choose max to cover entire target area
        var scaleX = (float)targetW / source.Width;
        var scaleY = (float)targetH / source.Height;
        var scale = Math.Max(scaleX, scaleY);

        var scaledW = Math.Max(1, (int)Math.Round(source.Width * scale));
        var scaledH = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var scaledBmp = Resize(source, scaledW, scaledH);

        // Crop centered rect
        var cropX = Math.Max(0, (scaledW - targetW) / 2);
        var cropY = Math.Max(0, (scaledH - targetH) / 2);

        var cropped = new SKBitmap(new SKImageInfo(targetW, targetH, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(cropped);
        canvas.Clear(SKColors.Transparent);

        var srcRect = new SKRect(cropX, cropY, cropX + targetW, cropY + targetH);
        var destRect = new SKRect(0, 0, targetW, targetH);
        canvas.DrawBitmap(scaledBmp, srcRect, destRect);

        return cropped;
    }

    private SKBitmap ResizePad(SKBitmap source, int targetW, int targetH, string padColorHex)
    {
        targetW = Math.Max(1, targetW);
        targetH = Math.Max(1, targetH);

        var bgColor = SKColor.TryParse(padColorHex, out var parsed) ? parsed : SKColors.White;

        // Proportional fit
        var scaleX = (float)targetW / source.Width;
        var scaleY = (float)targetH / source.Height;
        var scale = Math.Min(scaleX, scaleY);

        var fitW = Math.Max(1, (int)Math.Round(source.Width * scale));
        var fitH = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var scaledBmp = Resize(source, fitW, fitH);

        var result = new SKBitmap(new SKImageInfo(targetW, targetH, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(result);
        canvas.Clear(bgColor);

        var posX = (targetW - fitW) / 2f;
        var posY = (targetH - fitH) / 2f;
        canvas.DrawBitmap(scaledBmp, posX, posY);

        return result;
    }
}
