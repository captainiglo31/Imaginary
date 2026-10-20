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
        return DetectRegions(bitmap, detectFaces, detectPlates, minConfidence: 0.45f);
    }

    public IReadOnlyList<DetectedSensitiveRegion> DetectRegions(
        SKBitmap bitmap,
        bool detectFaces,
        bool detectPlates,
        float minConfidence)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var regions = new List<DetectedSensitiveRegion>();

        if (detectFaces)
        {
            regions.AddRange(DetectFaces(bitmap, minConfidence));
        }

        if (detectPlates)
        {
            regions.AddRange(DetectLicensePlates(bitmap, minConfidence));
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
        int intensity = 16,
        float minConfidence = 0.45f)
    {
        var detected = DetectRegions(bitmap, detectFaces, detectPlates, minConfidence);
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

    private static List<DetectedSensitiveRegion> DetectFaces(SKBitmap source, float minConfidence = 0.45f)
    {
        var results = new List<DetectedSensitiveRegion>();

        // Scale image for high-accuracy analysis (max 400px maintains eye-socket details)
        float scale = Math.Min(400f / source.Width, 400f / source.Height);
        if (scale > 1.0f) scale = 1.0f;

        int scanW = Math.Max(1, (int)(source.Width * scale));
        int scanH = Math.Max(1, (int)(source.Height * scale));

        using var small = source.Resize(new SKImageInfo(scanW, scanH, SKColorType.Bgra8888), SKFilterQuality.Medium);
        if (small == null) return results;

        // Precompute luminance and skin mask
        byte[,] gray = new byte[scanW, scanH];
        bool[,] skinMask = new bool[scanW, scanH];

        for (int y = 0; y < scanH; y++)
        {
            for (int x = 0; x < scanW; x++)
            {
                var c = small.GetPixel(x, y);
                gray[x, y] = (byte)(0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue);
                skinMask[x, y] = IsHumanSkinColor(c);
            }
        }

        // Multi-scale sliding window face detector with anthropometric face ratio (1 : 1.25)
        int[] windowSizes = {
            Math.Max(18, (int)(scanH * 0.12f)),
            Math.Max(26, (int)(scanH * 0.20f)),
            Math.Max(38, (int)(scanH * 0.32f)),
            Math.Max(54, (int)(scanH * 0.45f))
        };

        foreach (var winW in windowSizes)
        {
            int winH = (int)(winW * 1.25f);
            if (winW >= scanW || winH >= scanH) continue;
            int step = Math.Max(4, winW / 5);

            for (int y = 0; y <= scanH - winH; y += step)
            {
                for (int x = 0; x <= scanW - winW; x += step)
                {
                    int skinCount = 0;
                    int totalSamples = 0;

                    for (int dy = 0; dy < winH; dy += 2)
                    {
                        for (int dx = 0; dx < winW; dx += 2)
                        {
                            totalSamples++;
                            if (skinMask[x + dx, y + dy]) skinCount++;
                        }
                    }

                    float skinRatio = (float)skinCount / Math.Max(1, totalSamples);
                    if (skinRatio < 0.35f || skinRatio > 0.88f) continue;

                    // Anthropometric Face Verification:
                    // Zone 0: Forehead (y: 10% - 28%)
                    // Zone 1: Eyes / Brow ridge (y: 28% - 50%) - Must have darker features (pupils/sockets)
                    // Zone 2: Cheeks / Nose (y: 50% - 75%)
                    int foreheadSum = 0, foreheadCount = 0;
                    int eyesSum = 0, eyesCount = 0;
                    int cheeksSum = 0, cheeksCount = 0;

                    for (int dy = 0; dy < winH; dy++)
                    {
                        float yRel = (float)dy / winH;
                        for (int dx = (int)(winW * 0.15f); dx < (int)(winW * 0.85f); dx++)
                        {
                            byte lum = gray[x + dx, y + dy];
                            if (yRel >= 0.10f && yRel < 0.28f)
                            {
                                foreheadSum += lum;
                                foreheadCount++;
                            }
                            else if (yRel >= 0.28f && yRel < 0.50f)
                            {
                                eyesSum += lum;
                                eyesCount++;
                            }
                            else if (yRel >= 0.50f && yRel < 0.75f)
                            {
                                cheeksSum += lum;
                                cheeksCount++;
                            }
                        }
                    }

                    if (foreheadCount == 0 || eyesCount == 0 || cheeksCount == 0) continue;

                    float avgForehead = (float)foreheadSum / foreheadCount;
                    float avgEyes = (float)eyesSum / eyesCount;
                    float avgCheeks = (float)cheeksSum / cheeksCount;

                    // Eye socket luminance depression test (filters out uniform reflections and car seat leather)
                    float eyeDepression = Math.Max(avgForehead - avgEyes, avgCheeks - avgEyes);
                    if (eyeDepression < 3.5f) continue;

                    // Bilateral eye symmetry test in the eye band
                    int leftEyeSum = 0, leftEyeCount = 0;
                    int rightEyeSum = 0, rightEyeCount = 0;
                    int eyeYStart = y + (int)(winH * 0.30f);
                    int eyeYEnd = y + (int)(winH * 0.48f);

                    for (int cy = eyeYStart; cy < eyeYEnd; cy++)
                    {
                        for (int dx = (int)(winW * 0.15f); dx < (int)(winW * 0.42f); dx++)
                        {
                            leftEyeSum += gray[x + dx, cy];
                            leftEyeCount++;
                        }
                        for (int dx = (int)(winW * 0.58f); dx < (int)(winW * 0.85f); dx++)
                        {
                            rightEyeSum += gray[x + dx, cy];
                            rightEyeCount++;
                        }
                    }

                    float avgLeftEye = leftEyeCount > 0 ? (float)leftEyeSum / leftEyeCount : avgEyes;
                    float avgRightEye = rightEyeCount > 0 ? (float)rightEyeSum / rightEyeCount : avgEyes;
                    float eyeSymmetryDiff = Math.Abs(avgLeftEye - avgRightEye);

                    // If left and right eyes have wildly asymmetric brightness, reject (e.g. diagonal car reflection)
                    if (eyeSymmetryDiff > 35f) continue;

                    // Calculate confidence score
                    float confidence = Math.Clamp(skinRatio * 0.4f + (eyeDepression / 25f) * 0.4f + (1f - eyeSymmetryDiff / 35f) * 0.2f, 0.1f, 0.98f);
                    if (confidence < minConfidence) continue;

                    int origX = (int)(x / scale);
                    int origY = (int)(y / scale);
                    int origW = (int)(winW / scale);
                    int origH = (int)(winH / scale);

                    int padX = (int)(origW * 0.10f);
                    int padY = (int)(origH * 0.12f);

                    var bounds = new SKRectI(
                        Math.Max(0, origX - padX),
                        Math.Max(0, origY - padY),
                        Math.Min(source.Width, origX + origW + padX),
                        Math.Min(source.Height, origY + origH + padY));

                    results.Add(new DetectedSensitiveRegion(bounds, SensitiveRegionType.Face, confidence));
                }
            }
        }

        return results;
    }

    private static List<DetectedSensitiveRegion> DetectLicensePlates(SKBitmap source, float minConfidence = 0.45f)
    {
        var results = new List<DetectedSensitiveRegion>();

        float scale = Math.Min(600f / source.Width, 600f / source.Height);
        if (scale > 1.0f) scale = 1.0f;

        int scanW = Math.Max(1, (int)(source.Width * scale));
        int scanH = Math.Max(1, (int)(source.Height * scale));

        using var small = source.Resize(new SKImageInfo(scanW, scanH, SKColorType.Bgra8888), SKFilterQuality.Medium);
        if (small == null) return results;

        // Grayscale conversion
        byte[,] gray = new byte[scanW, scanH];
        for (int y = 0; y < scanH; y++)
        {
            for (int x = 0; x < scanW; x++)
            {
                var c = small.GetPixel(x, y);
                gray[x, y] = (byte)(0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue);
            }
        }

        // Search dimensions:
        // Standard EU / International single-line plates: aspect ratio ~4.7 : 1 (width 3.6 - 5.2x height)
        // Two-line / motorcycle / US plates: aspect ratio ~2.0 : 1
        (float aspect, float minH, float maxH)[] plateProfiles =
        {
            (4.7f, 0.035f, 0.13f), // Standard Euro / German single-line plate
            (2.1f, 0.05f, 0.16f)   // Two-line / US / square plate
        };

        foreach (var (aspectRatio, minRelH, maxRelH) in plateProfiles)
        {
            int hMin = Math.Max(7, (int)(scanH * minRelH));
            int hMax = Math.Min(scanH / 3, (int)(scanH * maxRelH));
            int hStep = Math.Max(4, (hMax - hMin) / 3);

            for (int pH = hMin; pH <= hMax; pH += hStep)
            {
                int pW = (int)(pH * aspectRatio);
                if (pW >= scanW - 4) continue;
                int stepX = Math.Max(4, pW / 12);
                int stepY = Math.Max(4, pH / 2);

                // Plates are on vehicles, almost exclusively in the lower 75% of an image
                int startY = (int)(scanH * 0.25f);
                int endY = scanH - pH - 2;

                for (int y = startY; y <= endY; y += stepY)
                {
                    for (int x = 2; x <= scanW - pW - 2; x += stepX)
                    {
                        // 1. Background Luminance & Character Contrast (Bimodal distribution)
                        // A plate has a bright white/yellow reflective background and dark printed characters.
                        int brightCount = 0;
                        int darkCount = 0;
                        int totalPixels = 0;
                        int lumSum = 0;
                        int maxLum = 0;

                        for (int dy = 1; dy < pH - 1; dy += 2)
                        {
                            for (int dx = 1; dx < pW - 1; dx += 2)
                            {
                                byte l = gray[x + dx, y + dy];
                                totalPixels++;
                                lumSum += l;
                                if (l > maxLum) maxLum = l;
                                if (l >= 135) brightCount++;
                                if (l <= 105) darkCount++;
                            }
                        }

                        if (totalPixels == 0) continue;

                        float avgLum = (float)lumSum / totalPixels;
                        float brightRatio = (float)brightCount / totalPixels;
                        float darkRatio = (float)darkCount / totalPixels;

                        // Plates MUST have a predominantly light background (white/yellow/silver plate)
                        // Wheels/tires/bumpers fail here: avgLum is low (< 95) and brightRatio is low (< 0.25)
                        if (avgLum < 118f || brightRatio < 0.38f) continue;

                        // Plates MUST contain dark characters: between 9% and 42% dark pixels
                        // Blank white surfaces, headlights, and glossy reflections fail here (darkRatio < 0.08)
                        if (darkRatio < 0.09f || darkRatio > 0.42f) continue;

                        // Reject blown-out pure specular glare (hotspots of headlights where center > 250 with no dark glyphs)
                        if (maxLum > 250 && darkRatio < 0.12f) continue;

                        // 2. Horizontal Alphanumeric Character Rhythm & Stroke Transitions
                        // License plates have distinct alphanumeric characters ("ERK SY 44")
                        // Horizontal scanlines through the plate middle exhibit multiple alternating dark/light transitions!
                        int transitionScore = 0;
                        int[] sampleYOffsets = { (int)(pH * 0.38f), (int)(pH * 0.50f), (int)(pH * 0.62f) };

                        foreach (var yOff in sampleYOffsets)
                        {
                            int scanY = y + yOff;
                            if (scanY >= scanH) continue;

                            int lineTransitions = 0;
                            bool isCurrentlyDark = gray[x + 1, scanY] <= 110;

                            for (int dx = 2; dx < pW - 1; dx++)
                            {
                                bool isDark = gray[x + dx, scanY] <= 110;
                                bool isBright = gray[x + dx, scanY] >= 135;

                                if (isCurrentlyDark && isBright)
                                {
                                    lineTransitions++;
                                    isCurrentlyDark = false;
                                }
                                else if (!isCurrentlyDark && isDark)
                                {
                                    lineTransitions++;
                                    isCurrentlyDark = true;
                                }
                            }

                            if (lineTransitions >= 5)
                            {
                                transitionScore += lineTransitions;
                            }
                        }

                        // A plate with text produces at least 12 transitions across the 3 sampled horizontal lines!
                        // Wheels have ~2-4 (spokes), headlights have 0-2 (smooth lens glass).
                        if (transitionScore < 10) continue;

                        // Calculate confidence score
                        float confidence = Math.Clamp(0.50f + (transitionScore / 40f) * 0.35f + (brightRatio * 0.15f), 0.50f, 0.98f);
                        if (confidence < minConfidence) continue;

                        int origX = (int)(x / scale);
                        int origY = (int)(y / scale);
                        int origW = (int)(pW / scale);
                        int origH = (int)(pH / scale);

                        int padX = (int)(origW * 0.06f);
                        int padY = (int)(origH * 0.10f);

                        var bounds = new SKRectI(
                            Math.Max(0, origX - padX),
                            Math.Max(0, origY - padY),
                            Math.Min(source.Width, origX + origW + padX),
                            Math.Min(source.Height, origY + origH + padY));

                        results.Add(new DetectedSensitiveRegion(bounds, SensitiveRegionType.LicensePlate, confidence));
                    }
                }
            }
        }

        return results;
    }

    private static bool IsHumanSkinColor(SKColor c)
    {
        // 1. Normalized RGB & YCbCr model
        // Y in [50, 240], Cb in [77, 130], Cr in [132, 178]
        int r = c.Red;
        int g = c.Green;
        int b = c.Blue;

        float y = 0.299f * r + 0.587f * g + 0.114f * b;
        if (y < 45f || y > 240f) return false;

        float cb = 128f - 0.168736f * r - 0.331264f * g + 0.5f * b;
        float cr = 128f + 0.5f * r - 0.418688f * g - 0.081312f * b;

        if (cb < 77f || cb > 130f || cr < 132f || cr > 180f) return false;

        float crDiffCb = cr - cb;
        if (crDiffCb < 10f || crDiffCb > 65f) return false;

        // 2. HSV Hue and Saturation limits
        c.ToHsv(out float h, out float s, out float v);
        bool hueOk = (h >= 0f && h <= 45f) || (h >= 335f && h <= 360f);
        if (!hueOk) return false;

        // Saturation check: Exclude high-saturation amber lights/turn signals and car paint (s > 0.70)
        // and desaturated grays/metals (s < 0.12)
        if (s < 0.12f || s > 0.70f) return false;

        // Value check: Exclude dark shadows or blown-out highlights
        if (v < 0.20f || v > 0.95f) return false;

        return true;
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
