using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Imaginary.Core.Logging;
using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class ProfiDeveloperService : IProfiDeveloperService
{
    private static readonly Lazy<IReadOnlyList<ProfiPreset>> _builtInPresets = new(CreateBuiltInPresets);

    public IReadOnlyList<ProfiPreset> GetBuiltInPresets() => _builtInPresets.Value;

    public SKBitmap LoadRawOrStandardBitmap(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("Datei wurde nicht gefunden.", filePath);

        // 1. Direkter SkiaSharp Decode Versuch
        try
        {
            using var stream = File.OpenRead(filePath);
            var bmp = SKBitmap.Decode(stream);
            if (bmp != null)
            {
                return Ensure32BitRgba(bmp);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Debug("ProfiDeveloper", $"SkiaSharp direkter Decode fehlgeschlagen für {Path.GetFileName(filePath)}: {ex.Message}");
        }

        // 2. Extraktion eingebetteter hochauflösender Vorschauen aus Kamera-RAW-Dateien (TIFF/EXIF/JPEG Streams)
        try
        {
            var rawBytes = File.ReadAllBytes(filePath);
            var extractedBmp = TryExtractEmbeddedRawPreview(rawBytes);
            if (extractedBmp != null)
            {
                AppLogger.Info("ProfiDeveloper", $"Eingebettete hochauflösende Vorschau aus RAW-Datei geladen: {Path.GetFileName(filePath)}");
                return Ensure32BitRgba(extractedBmp);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ProfiDeveloper", $"Konnte eingebettete RAW-Vorschau nicht extrahieren: {ex.Message}");
        }

        throw new InvalidOperationException($"Das Bild '{Path.GetFileName(filePath)}' konnte weder über SkiaSharp noch über den RAW-Extraktor geladen werden.");
    }

    private static SKBitmap Ensure32BitRgba(SKBitmap source)
    {
        if (source.ColorType == SKColorType.Rgba8888 || source.ColorType == SKColorType.Bgra8888)
        {
            return source;
        }

        var converted = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(converted);
        canvas.DrawBitmap(source, 0, 0);
        source.Dispose();
        return converted;
    }

    private static SKBitmap? TryExtractEmbeddedRawPreview(byte[] rawBytes)
    {
        // Suche nach JPEG Magic Header (FF D8 FF)
        // Kamera-RAWs wie CR2, NEF, ARW, DNG betten oft ein volles JPEG oder großes Preview-Bild ein.
        int maxScan = Math.Min(rawBytes.Length - 4, 16 * 1024 * 1024); // bis zu 16 MB scannen
        int largestJpegOffset = -1;
        int largestJpegLen = -1;

        for (int i = 0; i < maxScan; i++)
        {
            if (rawBytes[i] == 0xFF && rawBytes[i + 1] == 0xD8 && rawBytes[i + 2] == 0xFF)
            {
                // Suche das zugehörige Ende (FF D9)
                for (int j = i + 2048; j < rawBytes.Length - 1; j++)
                {
                    if (rawBytes[j] == 0xFF && rawBytes[j + 1] == 0xD9)
                    {
                        int len = j - i + 2;
                        if (len > largestJpegLen && len > 50_000) // Mindestens 50 KB, um kleine Thumbnails zu überspringen
                        {
                            largestJpegOffset = i;
                            largestJpegLen = len;
                        }
                        break;
                    }
                }
            }
        }

        if (largestJpegOffset >= 0 && largestJpegLen > 0)
        {
            using var ms = new MemoryStream(rawBytes, largestJpegOffset, largestJpegLen);
            return SKBitmap.Decode(ms);
        }

        return null;
    }

    public ImageHistogramData ComputeHistogram(SKBitmap bitmap)
    {
        var data = new ImageHistogramData();
        if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0) return data;

        int width = bitmap.Width;
        int height = bitmap.Height;
        int totalPixels = width * height;

        var red = data.Red;
        var green = data.Green;
        var blue = data.Blue;
        var luma = data.Luminance;

        // Schrittweite für Performance bei gigantischen Auflösungen
        int step = 1;
        if (totalPixels > 2_000_000) step = 2;
        if (totalPixels > 8_000_000) step = 3;

        int shadowClip = 0;
        int highlightClip = 0;
        int sampledPixels = 0;

        unsafe
        {
            var ptr = (byte*)bitmap.GetPixels().ToPointer();
            bool isBgra = bitmap.ColorType == SKColorType.Bgra8888;
            int bytesPerPixel = bitmap.BytesPerPixel;
            int rowBytes = bitmap.RowBytes;

            for (int y = 0; y < height; y += step)
            {
                byte* row = ptr + (y * rowBytes);
                for (int x = 0; x < width; x += step)
                {
                    byte* px = row + (x * bytesPerPixel);
                    byte b = isBgra ? px[0] : px[2];
                    byte g = px[1];
                    byte r = isBgra ? px[2] : px[0];

                    red[r]++;
                    green[g]++;
                    blue[b]++;

                    int yVal = (int)(0.2126f * r + 0.7152f * g + 0.0722f * b);
                    if (yVal > 255) yVal = 255;
                    luma[yVal]++;

                    if (r == 0 && g == 0 && b == 0) shadowClip++;
                    if (r >= 254 && g >= 254 && b >= 254) highlightClip++;
                    sampledPixels++;
                }
            }
        }

        int max = 0;
        for (int i = 0; i < 256; i++)
        {
            if (red[i] > max) max = red[i];
            if (green[i] > max) max = green[i];
            if (blue[i] > max) max = blue[i];
            if (luma[i] > max) max = luma[i];
        }

        data.MaxValue = Math.Max(1, max);
        data.ShadowClippingCount = shadowClip;
        data.HighlightClippingCount = highlightClip;
        data.ShadowClippingPercentage = sampledPixels > 0 ? (double)shadowClip / sampledPixels * 100.0 : 0;
        data.HighlightClippingPercentage = sampledPixels > 0 ? (double)highlightClip / sampledPixels * 100.0 : 0;

        return data;
    }

    public SKBitmap ApplyAdjustments(SKBitmap source, ProfiAdjustmentSettings s)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (s == null || s.IsDefault)
        {
            return source.Copy();
        }

        int width = source.Width;
        int height = source.Height;

        // 1. Look-Up Tables (LUTs) vorberechnen für R, G, B
        byte[] lutR = new byte[256];
        byte[] lutG = new byte[256];
        byte[] lutB = new byte[256];
        BuildChannelLuts(s, lutR, lutG, lutB);

        // 2. Ziel-Bitmap erstellen
        var result = new SKBitmap(width, height, source.ColorType, source.AlphaType);

        bool hasColorGrading = s.ShadowSaturation > 0 || s.HighlightSaturation > 0;
        bool hasVibranceOrSat = s.Vibrance != 0 || s.Saturation != 0 || s.HueShift != 0;
        bool hasVignette = s.Vignette != 0;
        bool hasGrain = s.Grain > 0;

        float centerX = width * 0.5f;
        float centerY = height * 0.5f;
        float maxDistSq = (centerX * centerX) + (centerY * centerY);
        float vignetteMult = s.Vignette / 100.0f; // -1 to +1

        // Precompute grading colors
        float shHue = s.ShadowHue;
        float shSat = s.ShadowSaturation / 100.0f;
        float hlHue = s.HighlightHue;
        float hlSat = s.HighlightSaturation / 100.0f;
        float gradeBal = s.Balance / 100.0f; // -1 to +1

        SKColor shRgb = HsvToRgb(shHue, shSat, 1.0f);
        SKColor hlRgb = HsvToRgb(hlHue, hlSat, 1.0f);

        float vib = s.Vibrance / 100.0f;
        float sat = (s.Saturation + 100.0f) / 100.0f; // 0 (mono) to 2.0 (super-sat)
        float hueAngle = s.HueShift;

        unsafe
        {
            var srcPtr = (byte*)source.GetPixels().ToPointer();
            var dstPtr = (byte*)result.GetPixels().ToPointer();
            int bpp = source.BytesPerPixel;
            int srcRowBytes = source.RowBytes;
            int dstRowBytes = result.RowBytes;
            bool isBgra = source.ColorType == SKColorType.Bgra8888;

            Parallel.For(0, height, y =>
            {
                byte* srcRow = srcPtr + (y * srcRowBytes);
                byte* dstRow = dstPtr + (y * dstRowBytes);

                // Pseudo-Random Seed pro Zeile für Grain
                uint rng = (uint)(y * 19999 + 67283);

                for (int x = 0; x < width; x++)
                {
                    byte* spx = srcRow + (x * bpp);
                    byte* dpx = dstRow + (x * bpp);

                    byte b = isBgra ? spx[0] : spx[2];
                    byte g = spx[1];
                    byte r = isBgra ? spx[2] : spx[0];
                    byte a = (bpp == 4) ? spx[3] : (byte)255;

                    // Stufe 1: Tonwert- & Weißabgleich-LUT anwenden
                    float rf = lutR[r];
                    float gf = lutG[g];
                    float bf = lutB[b];

                    // Stufe 2: Split-Toning / Color Grading
                    if (hasColorGrading)
                    {
                        float lum = (0.2126f * rf + 0.7152f * gf + 0.0722f * bf) / 255.0f;
                        float mid = 0.5f + (gradeBal * 0.25f);

                        // Schatten-Tönung (mehr bei dunklen Pixeln)
                        if (shSat > 0)
                        {
                            float shadowWeight = Math.Clamp((mid - lum) / mid, 0.0f, 1.0f) * shSat * 0.6f;
                            rf = rf * (1.0f - shadowWeight) + shRgb.Red * shadowWeight;
                            gf = gf * (1.0f - shadowWeight) + shRgb.Green * shadowWeight;
                            bf = bf * (1.0f - shadowWeight) + shRgb.Blue * shadowWeight;
                        }

                        // Lichter-Tönung (mehr bei hellen Pixeln)
                        if (hlSat > 0)
                        {
                            float hlWeight = Math.Clamp((lum - mid) / (1.0f - mid), 0.0f, 1.0f) * hlSat * 0.6f;
                            rf = rf * (1.0f - hlWeight) + hlRgb.Red * hlWeight;
                            gf = gf * (1.0f - hlWeight) + hlRgb.Green * hlWeight;
                            bf = bf * (1.0f - hlWeight) + hlRgb.Blue * hlWeight;
                        }
                    }

                    // Stufe 3: Dynamik (Vibrance), Sättigung & Farbton (Hue Shift)
                    if (hasVibranceOrSat)
                    {
                        if (hueAngle != 0)
                        {
                            RgbToHsv(rf, gf, bf, out float h, out float sVal, out float vVal);
                            h = (h + hueAngle + 360.0f) % 360.0f;
                            var rgb = HsvToRgb(h, sVal, vVal);
                            rf = rgb.Red;
                            gf = rgb.Green;
                            bf = rgb.Blue;
                        }

                        float curLum = 0.2126f * rf + 0.7152f * gf + 0.0722f * bf;
                        float maxC = Math.Max(rf, Math.Max(gf, bf));
                        float minC = Math.Min(rf, Math.Min(gf, bf));
                        float curSat = maxC > 0.001f ? (maxC - minC) / maxC : 0.0f;

                        // Intelligente Dynamik: boostet ungesättigte Farben stärker als bereits gesättigte Farben
                        float effectiveSatMult = sat;
                        if (vib != 0)
                        {
                            float vibFactor = vib * (1.0f - curSat * 0.85f);
                            effectiveSatMult += vibFactor;
                        }
                        if (effectiveSatMult < 0.0f) effectiveSatMult = 0.0f;

                        rf = curLum + (rf - curLum) * effectiveSatMult;
                        gf = curLum + (gf - curLum) * effectiveSatMult;
                        bf = curLum + (bf - curLum) * effectiveSatMult;
                    }

                    // Stufe 4: Vignettierung
                    if (hasVignette)
                    {
                        float dx = x - centerX;
                        float dy = y - centerY;
                        float distNorm = ((dx * dx) + (dy * dy)) / maxDistSq;
                        distNorm = Math.Clamp(distNorm, 0.0f, 1.0f);

                        // Weicher Abfall nach außen
                        float vFactor = 1.0f + (vignetteMult * distNorm * distNorm * 1.25f);
                        rf *= vFactor;
                        gf *= vFactor;
                        bf *= vFactor;
                    }

                    // Stufe 5: Analoges Filmkorn (Grain)
                    if (hasGrain)
                    {
                        // XorShift PRNG
                        rng ^= rng << 13;
                        rng ^= rng >> 17;
                        rng ^= rng << 5;
                        float noise = ((rng & 0xFFFF) / 65535.0f) - 0.5f; // -0.5 bis +0.5

                        // Filmkorn ist in den Mitteltönen am stärksten sichtbar
                        float lumNorm = Math.Clamp((0.2126f * rf + 0.7152f * gf + 0.0722f * bf) / 255.0f, 0.0f, 1.0f);
                        float grainMask = 1.0f - Math.Abs(lumNorm - 0.5f) * 1.4f;
                        if (grainMask < 0) grainMask = 0;

                        float grainOffset = noise * (s.Grain * 0.7f) * grainMask;
                        rf += grainOffset;
                        gf += grainOffset;
                        bf += grainOffset;
                    }

                    // Clamping auf 0-255
                    byte outR = (byte)Math.Clamp((int)(rf + 0.5f), 0, 255);
                    byte outG = (byte)Math.Clamp((int)(gf + 0.5f), 0, 255);
                    byte outB = (byte)Math.Clamp((int)(bf + 0.5f), 0, 255);

                    if (isBgra)
                    {
                        dpx[0] = outB;
                        dpx[1] = outG;
                        dpx[2] = outR;
                    }
                    else
                    {
                        dpx[0] = outR;
                        dpx[1] = outG;
                        dpx[2] = outB;
                    }

                    if (bpp == 4) dpx[3] = a;
                }
            });
        }

        // Stufe 6: Schärfen (Unsharp Mask) oder Klarheit / Rauschreduzierung falls aktiviert
        if (s.Sharpen > 0 || s.Clarity != 0 || s.NoiseReduction > 0)
        {
            var refined = ApplyDetailFilters(result, s);
            result.Dispose();
            return refined;
        }

        return result;
    }

    private static void BuildChannelLuts(ProfiAdjustmentSettings s, byte[] lutR, byte[] lutG, byte[] lutB)
    {
        // 1. Belichtung (EV): 2^EV
        float exposureMult = MathF.Pow(2.0f, s.Exposure);

        // 2. Kontrastfaktor: -100 bis +100
        float c = s.Contrast / 100.0f;
        float contrastFactor = (1.0f + c) / (1.0f - Math.Min(c, 0.99f));

        // 3. Lichter & Tiefen
        float hlFactor = s.Highlights / 100.0f;
        float shFactor = s.Shadows / 100.0f;
        float wFactor = s.Whites / 100.0f;
        float bFactor = s.Blacks / 100.0f;

        // 4. Weißabgleich (Temperatur: Kalt/Blau <-> Warm/Gelb, Tönung: Grün <-> Magenta)
        float temp = s.Temperature / 100.0f;
        float tint = s.Tint / 100.0f;

        float rMult = 1.0f + (temp * 0.28f) + (tint * 0.15f);
        float gMult = 1.0f - (tint * 0.22f);
        float bMult = 1.0f - (temp * 0.28f) + (tint * 0.12f);

        // 5. Kurven-Korrekturen
        float curveSh = s.CurveShadows / 100.0f * 35.0f;
        float curveDk = s.CurveDarks / 100.0f * 35.0f;
        float curveLt = s.CurveLights / 100.0f * 35.0f;
        float curveHl = s.CurveHighlights / 100.0f * 35.0f;

        for (int i = 0; i < 256; i++)
        {
            float val = i;

            // Belichtung
            val *= exposureMult;

            // Tiefen & Lichter Korrektur
            float norm = Math.Clamp(val / 255.0f, 0.0f, 1.0f);

            // Schatten aufhellen/vertiefen (stärkste Auswirkung bei norm < 0.5)
            if (shFactor != 0)
            {
                float shWeight = MathF.Pow(1.0f - norm, 2.0f);
                val += shFactor * shWeight * 50.0f;
            }

            // Lichter abdunkeln/retten (stärkste Auswirkung bei norm > 0.5)
            if (hlFactor != 0)
            {
                float hlWeight = MathF.Pow(norm, 2.0f);
                val += hlFactor * hlWeight * 45.0f;
            }

            // Weißwert / Schwarzwert
            if (bFactor != 0)
            {
                float bWeight = Math.Clamp(1.0f - (norm * 2.0f), 0.0f, 1.0f);
                val += bFactor * bWeight * 30.0f;
            }
            if (wFactor != 0)
            {
                float wWeight = Math.Clamp((norm - 0.5f) * 2.0f, 0.0f, 1.0f);
                val += wFactor * wWeight * 30.0f;
            }

            // Kontrast (S-Kurve zentriert bei 128)
            val = 128.0f + (val - 128.0f) * contrastFactor;

            // Tonwertkurve Zonen
            if (curveSh != 0 || curveDk != 0 || curveLt != 0 || curveHl != 0)
            {
                float pNorm = Math.Clamp(val / 255.0f, 0.0f, 1.0f);
                float w0 = MathF.Pow(1.0f - pNorm, 3.0f);
                float w1 = 3.0f * pNorm * MathF.Pow(1.0f - pNorm, 2.0f);
                float w2 = 3.0f * (pNorm * pNorm) * (1.0f - pNorm);
                float w3 = MathF.Pow(pNorm, 3.0f);
                val += (curveSh * w0) + (curveDk * w1) + (curveLt * w2) + (curveHl * w3);
            }

            // Farbtemperatur & Tönung pro Kanal
            lutR[i] = (byte)Math.Clamp((int)(val * rMult + 0.5f), 0, 255);
            lutG[i] = (byte)Math.Clamp((int)(val * gMult + 0.5f), 0, 255);
            lutB[i] = (byte)Math.Clamp((int)(val * bMult + 0.5f), 0, 255);
        }
    }

    private static SKBitmap ApplyDetailFilters(SKBitmap input, ProfiAdjustmentSettings s)
    {
        int width = input.Width;
        int height = input.Height;
        var output = new SKBitmap(width, height, input.ColorType, input.AlphaType);

        float sharpenAmount = s.Sharpen / 100.0f; // 0 bis 1.0
        float clarityAmount = s.Clarity / 100.0f; // -1 bis +1
        float denoiseAmount = s.NoiseReduction / 100.0f; // 0 bis 1.0

        int bpp = input.BytesPerPixel;
        int rowBytes = input.RowBytes;

        unsafe
        {
            var srcPtr = (byte*)input.GetPixels().ToPointer();
            var dstPtr = (byte*)output.GetPixels().ToPointer();

            Parallel.For(0, height, y =>
            {
                int yTop = Math.Max(0, y - 1);
                int yBottom = Math.Min(height - 1, y + 1);

                byte* srcRow = srcPtr + (y * rowBytes);
                byte* srcTop = srcPtr + (yTop * rowBytes);
                byte* srcBottom = srcPtr + (yBottom * rowBytes);
                byte* dstRow = dstPtr + (y * rowBytes);

                for (int x = 0; x < width; x++)
                {
                    int xLeft = Math.Max(0, x - 1);
                    int xRight = Math.Min(width - 1, x + 1);

                    byte* pxCenter = srcRow + (x * bpp);
                    byte* pxLeft = srcRow + (xLeft * bpp);
                    byte* pxRight = srcRow + (xRight * bpp);
                    byte* pxTop = srcTop + (x * bpp);
                    byte* pxBottom = srcBottom + (x * bpp);

                    byte* dpx = dstRow + (x * bpp);

                    for (int c = 0; c < 3; c++)
                    {
                        float centerVal = pxCenter[c];
                        float avgNeighbors = (pxLeft[c] + pxRight[c] + pxTop[c] + pxBottom[c]) * 0.25f;
                        float diff = centerVal - avgNeighbors;

                        float res = centerVal;

                        // Schärfen: Kanten verstärken
                        if (sharpenAmount > 0)
                        {
                            res += diff * (sharpenAmount * 1.6f);
                        }

                        // Klarheit (Mikrokontrast): Mittelton-Kanten betonen
                        if (clarityAmount != 0)
                        {
                            float norm = centerVal / 255.0f;
                            float midtoneWeight = 1.0f - Math.Abs(norm - 0.5f) * 1.8f;
                            if (midtoneWeight > 0)
                            {
                                res += diff * (clarityAmount * midtoneWeight * 1.4f);
                            }
                        }

                        // Rauschunterdrückung: Feine Schwankungen glätten
                        if (denoiseAmount > 0)
                        {
                            if (Math.Abs(diff) < 24.0f)
                            {
                                res = centerVal * (1.0f - denoiseAmount * 0.7f) + avgNeighbors * (denoiseAmount * 0.7f);
                            }
                        }

                        dpx[c] = (byte)Math.Clamp((int)(res + 0.5f), 0, 255);
                    }

                    if (bpp == 4) dpx[3] = pxCenter[3];
                }
            });
        }

        return output;
    }

    private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float min = Math.Min(r, Math.Min(g, b));
        float max = Math.Max(r, Math.Max(g, b));
        v = max / 255.0f;
        float delta = max - min;

        if (max <= 0.0001f || delta <= 0.0001f)
        {
            s = 0;
            h = 0;
            return;
        }

        s = delta / max;
        if (Math.Abs(r - max) < 0.001f)
            h = (g - b) / delta;
        else if (Math.Abs(g - max) < 0.001f)
            h = 2.0f + (b - r) / delta;
        else
            h = 4.0f + (r - g) / delta;

        h *= 60.0f;
        if (h < 0) h += 360.0f;
    }

    private static SKColor HsvToRgb(float h, float s, float v)
    {
        h = (h % 360.0f + 360.0f) % 360.0f;
        s = Math.Clamp(s, 0.0f, 1.0f);
        v = Math.Clamp(v, 0.0f, 1.0f);

        float c = v * s;
        float x = c * (1.0f - Math.Abs((h / 60.0f) % 2.0f - 1.0f));
        float m = v - c;

        float rPrime = 0, gPrime = 0, bPrime = 0;
        if (h < 60) { rPrime = c; gPrime = x; }
        else if (h < 120) { rPrime = x; gPrime = c; }
        else if (h < 180) { gPrime = c; bPrime = x; }
        else if (h < 240) { gPrime = x; bPrime = c; }
        else if (h < 300) { rPrime = x; bPrime = c; }
        else { rPrime = c; bPrime = x; }

        byte r = (byte)Math.Clamp((int)((rPrime + m) * 255.0f + 0.5f), 0, 255);
        byte g = (byte)Math.Clamp((int)((gPrime + m) * 255.0f + 0.5f), 0, 255);
        byte b = (byte)Math.Clamp((int)((bPrime + m) * 255.0f + 0.5f), 0, 255);

        return new SKColor(r, g, b);
    }

    private static IReadOnlyList<ProfiPreset> CreateBuiltInPresets()
    {
        return new List<ProfiPreset>
        {
            new()
            {
                Id = "neutral",
                Name = "Original / Neutral",
                Description = "Unverändertes Bild ohne Farbanpassungen.",
                Icon = "⚪",
                Settings = new ProfiAdjustmentSettings()
            },
            new()
            {
                Id = "vivid",
                Name = "Vivid Pop / Kräftig",
                Description = "Lebendige Farben, knackiger Kontrast und gerettete Spitzlichter.",
                Icon = "🌈",
                Settings = new ProfiAdjustmentSettings
                {
                    Exposure = 0.10f,
                    Contrast = 18f,
                    Highlights = -16f,
                    Shadows = 18f,
                    Whites = 10f,
                    Vibrance = 28f,
                    Saturation = 8f,
                    Clarity = 16f,
                    Sharpen = 20f
                }
            },
            new()
            {
                Id = "teal_orange",
                Name = "Cinema Teal & Orange",
                Description = "Klassischer Hollywood-Film-Look mit kühlen Schatten und warmen Hauttönen.",
                Icon = "🎬",
                Settings = new ProfiAdjustmentSettings
                {
                    Exposure = 0.05f,
                    Contrast = 16f,
                    Shadows = 15f,
                    Highlights = -10f,
                    ShadowHue = 205f,
                    ShadowSaturation = 32f,
                    HighlightHue = 38f,
                    HighlightSaturation = 26f,
                    Clarity = 14f,
                    Vignette = -18f
                }
            },
            new()
            {
                Id = "moody_urban",
                Name = "Moody Urban / Street",
                Description = "Dramatisch entsättigte Großstadtstimmung mit tiefen Schatten.",
                Icon = "🏙️",
                Settings = new ProfiAdjustmentSettings
                {
                    Exposure = -0.15f,
                    Contrast = 26f,
                    Highlights = -30f,
                    Shadows = -12f,
                    Blacks = -16f,
                    Saturation = -25f,
                    Clarity = 28f,
                    Dehaze = 10f,
                    Vignette = -26f,
                    Grain = 15f
                }
            },
            new()
            {
                Id = "golden_hour",
                Name = "Golden Hour / Warmes Licht",
                Description = "Goldenes Abendlicht mit warmen Farben und sanften Schatten.",
                Icon = "🌅",
                Settings = new ProfiAdjustmentSettings
                {
                    Exposure = 0.15f,
                    Temperature = 25f,
                    Tint = 6f,
                    Highlights = -18f,
                    Shadows = 20f,
                    HighlightHue = 42f,
                    HighlightSaturation = 22f,
                    Vibrance = 18f,
                    Clarity = 8f
                }
            },
            new()
            {
                Id = "arctic_cold",
                Name = "Arktisch Kühl (Cold Fade)",
                Description = "Kühle, klare Atmosphäre mit betonten Blautönen.",
                Icon = "❄️",
                Settings = new ProfiAdjustmentSettings
                {
                    Temperature = -32f,
                    Tint = -6f,
                    Highlights = 15f,
                    Shadows = 10f,
                    Saturation = -12f,
                    Vibrance = 12f,
                    Vignette = -12f
                }
            },
            new()
            {
                Id = "vintage_film",
                Name = "Analog / 35mm Vintage",
                Description = "Nostalgischer Retro-Film-Look mit angehobenen Schwarztönen und sanftem Korn.",
                Icon = "🎞️",
                Settings = new ProfiAdjustmentSettings
                {
                    Contrast = -10f,
                    Blacks = 20f,
                    Shadows = 16f,
                    Temperature = 10f,
                    Tint = 6f,
                    Saturation = -16f,
                    Grain = 35f,
                    Vignette = -20f
                }
            },
            new()
            {
                Id = "dramatic_bw",
                Name = "Dramatisch B&W (Fine Art)",
                Description = "Klassisches Schwarz-Weiß mit hohem Kontrast und kräftigen Schatten.",
                Icon = "🖤",
                Settings = new ProfiAdjustmentSettings
                {
                    Saturation = -100f,
                    Contrast = 38f,
                    Highlights = -22f,
                    Shadows = 24f,
                    Whites = 16f,
                    Blacks = -20f,
                    Clarity = 32f,
                    Sharpen = 25f,
                    Grain = 22f,
                    Vignette = -28f
                }
            },
            new()
            {
                Id = "hdr_landscape",
                Name = "HDR Landschaft",
                Description = "Maximaler Dynamikumfang für Natur- und Landschaftsaufnahmen.",
                Icon = "🌄",
                Settings = new ProfiAdjustmentSettings
                {
                    Exposure = 0.05f,
                    Highlights = -55f,
                    Shadows = 55f,
                    Whites = 16f,
                    Blacks = -10f,
                    Contrast = 14f,
                    Clarity = 34f,
                    Dehaze = 18f,
                    Vibrance = 28f,
                    Sharpen = 30f
                }
            },
            new()
            {
                Id = "portrait_soft",
                Name = "Porträt Sanft (Glow & Skin)",
                Description = "Schmeichelnder Look für Gesichter mit sanftem Glow und warmem Teint.",
                Icon = "👤",
                Settings = new ProfiAdjustmentSettings
                {
                    Exposure = 0.18f,
                    Contrast = -8f,
                    Highlights = -18f,
                    Shadows = 16f,
                    Vibrance = 14f,
                    Clarity = -12f,
                    Temperature = 8f
                }
            }
        };
    }
}
