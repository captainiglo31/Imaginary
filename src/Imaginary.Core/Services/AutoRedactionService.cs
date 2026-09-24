using System;
using System.Collections.Generic;
using System.Linq;
using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class AutoRedactionService : IAutoRedactionService
{
    public IReadOnlyList<DetectedSensitiveRegion> DetectRegions(
        SKBitmap bitmap,
        bool detectFaces = true,
        bool detectPlates = true)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var regions = new List<DetectedSensitiveRegion>();

        if (detectFaces)
        {
            regions.AddRange(DetectFaces(bitmap));
        }

        if (detectPlates)
        {
            regions.AddRange(DetectLicensePlates(bitmap));
        }

        return NonMaxSuppression(regions);
    }

    public SKBitmap RedactRegions(
        SKBitmap bitmap,
        IEnumerable<DetectedSensitiveRegion> regions,
        RedactionStyle style = RedactionStyle.Pixelate,
        int intensity = 16)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var result = bitmap.Copy();

        using var canvas = new SKCanvas(result);

        foreach (var reg in regions)
        {
            var bounds = ClampRect(reg.Bounds, bitmap.Width, bitmap.Height);
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;

            ApplyRedaction(canvas, result, bounds, style, intensity);
        }

        canvas.Flush();
        return result;
    }

    public SKBitmap AutoRedact(
        SKBitmap bitmap,
        bool detectFaces = true,
        bool detectPlates = true,
        RedactionStyle style = RedactionStyle.Pixelate,
        int intensity = 16)
    {
        var detected = DetectRegions(bitmap, detectFaces, detectPlates);
        return RedactRegions(bitmap, detected, style, intensity);
    }

    private static void ApplyRedaction(
        SKCanvas canvas,
        SKBitmap targetBitmap,
        SKRectI bounds,
        RedactionStyle style,
        int intensity)
    {
        int blockSize = Math.Clamp(intensity, 4, 64);

        if (style == RedactionStyle.Blackout)
        {
            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), paint);
        }
        else if (style == RedactionStyle.Blur)
        {
            // Extract patch, blur and draw back
            using var patch = new SKBitmap(bounds.Width, bounds.Height);
            targetBitmap.ExtractSubset(patch, bounds);

            using var blurPaint = new SKPaint
            {
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(blockSize, blockSize)
            };
            canvas.DrawBitmap(patch, bounds.Left, bounds.Top, blurPaint);
        }
        else // Pixelate / Mosaic
        {
            using var patch = new SKBitmap(bounds.Width, bounds.Height);
            targetBitmap.ExtractSubset(patch, bounds);

            int smallW = Math.Max(1, bounds.Width / blockSize);
            int smallH = Math.Max(1, bounds.Height / blockSize);

            using var small = patch.Resize(new SKImageInfo(smallW, smallH, SKColorType.Bgra8888), SKFilterQuality.None);
            if (small != null)
            {
                using var pixelated = small.Resize(new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Bgra8888), SKFilterQuality.None);
                if (pixelated != null)
                {
                    canvas.DrawBitmap(pixelated, bounds.Left, bounds.Top);
                }
            }
        }
    }

    private static List<DetectedSensitiveRegion> DetectFaces(SKBitmap source)
    {
        var results = new List<DetectedSensitiveRegion>();

        // Downscale to max 320 for high performance scanning
        float scale = Math.Min(320f / source.Width, 320f / source.Height);
        if (scale > 1.0f) scale = 1.0f;

        int scanW = Math.Max(1, (int)(source.Width * scale));
        int scanH = Math.Max(1, (int)(source.Height * scale));

        using var small = source.Resize(new SKImageInfo(scanW, scanH, SKColorType.Bgra8888), SKFilterQuality.Low);
        if (small == null) return results;

        // Binary skin mask
        bool[,] skinMask = new bool[scanW, scanH];
        for (int y = 0; y < scanH; y++)
        {
            for (int x = 0; x < scanW; x++)
            {
                var c = small.GetPixel(x, y);
                skinMask[x, y] = IsSkinColor(c);
            }
        }

        // Multi-scale sliding window face detector
        int[] windowSizes = { (int)(scanH * 0.15f), (int)(scanH * 0.25f), (int)(scanH * 0.38f) };

        foreach (var winSize in windowSizes)
        {
            if (winSize < 16) continue;
            int winH = (int)(winSize * 1.25f); // Faces are roughly 1:1.25
            int step = Math.Max(4, winSize / 4);

            for (int y = 0; y <= scanH - winH; y += step)
            {
                for (int x = 0; x <= scanW - winSize; x += step)
                {
                    int skinPixels = 0;
                    int total = winSize * winH;

                    for (int dy = 0; dy < winH; dy += 2)
                    {
                        for (int dx = 0; dx < winSize; dx += 2)
                        {
                            if (skinMask[x + dx, y + dy]) skinPixels += 4;
                        }
                    }

                    float ratio = (float)skinPixels / total;
                    if (ratio >= 0.42f && ratio <= 0.88f)
                    {
                        // Map back to original coordinate system with safety padding
                        int origX = (int)(x / scale);
                        int origY = (int)(y / scale);
                        int origW = (int)(winSize / scale);
                        int origH = (int)(winH / scale);

                        int padX = (int)(origW * 0.12f);
                        int padY = (int)(origH * 0.12f);

                        var bounds = new SKRectI(
                            Math.Max(0, origX - padX),
                            Math.Max(0, origY - padY),
                            Math.Min(source.Width, origX + origW + padX),
                            Math.Min(source.Height, origY + origH + padY));

                        results.Add(new DetectedSensitiveRegion(bounds, SensitiveRegionType.Face, ratio));
                    }
                }
            }
        }

        return results;
    }

    private static List<DetectedSensitiveRegion> DetectLicensePlates(SKBitmap source)
    {
        var results = new List<DetectedSensitiveRegion>();

        float scale = Math.Min(400f / source.Width, 400f / source.Height);
        if (scale > 1.0f) scale = 1.0f;

        int scanW = Math.Max(1, (int)(source.Width * scale));
        int scanH = Math.Max(1, (int)(source.Height * scale));

        using var small = source.Resize(new SKImageInfo(scanW, scanH, SKColorType.Bgra8888), SKFilterQuality.Low);
        if (small == null) return results;

        // Grayscale & horizontal edge density
        byte[,] edges = new byte[scanW, scanH];
        for (int y = 1; y < scanH - 1; y++)
        {
            for (int x = 1; x < scanW - 1; x++)
            {
                var left = small.GetPixel(x - 1, y);
                var right = small.GetPixel(x + 1, y);
                int grayL = (left.Red + left.Green + left.Blue) / 3;
                int grayR = (right.Red + right.Green + right.Blue) / 3;
                edges[x, y] = (byte)Math.Clamp(Math.Abs(grayL - grayR), 0, 255);
            }
        }

        // License plates: typical aspect ratio ~3.5:1 (width:height)
        int[] plateHeights = { Math.Max(8, (int)(scanH * 0.05f)), Math.Max(12, (int)(scanH * 0.09f)), Math.Max(16, (int)(scanH * 0.14f)) };

        foreach (var pH in plateHeights)
        {
            int pW = (int)(pH * 3.6f);
            if (pW >= scanW) continue;
            int step = Math.Max(4, pH / 2);

            for (int y = (int)(scanH * 0.25f); y <= scanH - pH - 4; y += step) // Plates are rarely in the top 25% of an image
            {
                for (int x = 4; x <= scanW - pW - 4; x += step)
                {
                    int highEdgeCount = 0;
                    int samples = 0;

                    for (int dy = 0; dy < pH; dy += 2)
                    {
                        for (int dx = 0; dx < pW; dx += 2)
                        {
                            samples++;
                            if (edges[x + dx, y + dy] > 32)
                            {
                                highEdgeCount++;
                            }
                        }
                    }

                    float edgeDensity = (float)highEdgeCount / Math.Max(1, samples);

                    // High frequency horizontal edge density characteristic of alphanumeric characters on plates
                    if (edgeDensity > 0.38f && edgeDensity < 0.85f)
                    {
                        int origX = (int)(x / scale);
                        int origY = (int)(y / scale);
                        int origW = (int)(pW / scale);
                        int origH = (int)(pH / scale);

                        int padX = (int)(origW * 0.08f);
                        int padY = (int)(origH * 0.15f);

                        var bounds = new SKRectI(
                            Math.Max(0, origX - padX),
                            Math.Max(0, origY - padY),
                            Math.Min(source.Width, origX + origW + padX),
                            Math.Min(source.Height, origY + origH + padY));

                        results.Add(new DetectedSensitiveRegion(bounds, SensitiveRegionType.LicensePlate, edgeDensity));
                    }
                }
            }
        }

        return results;
    }

    private static bool IsSkinColor(SKColor c)
    {
        // Standard normalized RGB and HSV skin tone heuristic
        int r = c.Red;
        int g = c.Green;
        int b = c.Blue;

        bool rule1 = r > 95 && g > 40 && b > 20 &&
                     (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))) > 15 &&
                     Math.Abs(r - g) > 15 && r > g && r > b;

        return rule1;
    }

    private static List<DetectedSensitiveRegion> NonMaxSuppression(List<DetectedSensitiveRegion> boxes, float iouThreshold = 0.35f)
    {
        if (boxes.Count <= 1) return boxes;

        var sorted = boxes.OrderByDescending(b => b.Confidence).ToList();
        var selected = new List<DetectedSensitiveRegion>();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            selected.Add(best);
            sorted.RemoveAt(0);

            sorted.RemoveAll(item =>
            {
                if (item.Type != best.Type) return false;
                float iou = ComputeIoU(best.Bounds, item.Bounds);
                return iou >= iouThreshold;
            });
        }

        return selected;
    }

    private static float ComputeIoU(SKRectI a, SKRectI b)
    {
        int interLeft = Math.Max(a.Left, b.Left);
        int interTop = Math.Max(a.Top, b.Top);
        int interRight = Math.Min(a.Right, b.Right);
        int interBottom = Math.Min(a.Bottom, b.Bottom);

        int interArea = Math.Max(0, interRight - interLeft) * Math.Max(0, interBottom - interTop);
        int areaA = a.Width * a.Height;
        int areaB = b.Width * b.Height;

        int unionArea = areaA + areaB - interArea;
        if (unionArea <= 0) return 0f;

        return (float)interArea / unionArea;
    }

    private static SKRectI ClampRect(SKRectI r, int maxW, int maxH)
    {
        int left = Math.Clamp(r.Left, 0, maxW);
        int top = Math.Clamp(r.Top, 0, maxH);
        int right = Math.Clamp(r.Right, 0, maxW);
        int bottom = Math.Clamp(r.Bottom, 0, maxH);

        return new SKRectI(left, top, right, bottom);
    }
}
