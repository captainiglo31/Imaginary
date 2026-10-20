using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Imaginary.Core.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class UpscaleService : IUpscaleService
{
    private const string Model2xFileName = "realesrgan_x2.onnx";
    private const string Model4xFileName = "realesrgan_x4.onnx";

    private const string Model2xUrl = "https://huggingface.co/tidus2102/Real-ESRGAN/resolve/main/Real-ESRGAN_x2plus.onnx";
    private const string Model4xUrl = "https://huggingface.co/skillsafe-ai/realesrgan-x4plus/resolve/main/model.onnx";

    private readonly HttpClient _httpClient;

    public UpscaleService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public string GetModelPath(int factor = 2)
    {
        var fileName = factor == 4 ? Model4xFileName : Model2xFileName;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Imaginary",
            "Models");

        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return Path.Combine(dir, fileName);
    }

    public bool IsAiModelDownloaded(int factor = 2)
    {
        var path = GetModelPath(factor);
        if (!File.Exists(path)) return false;
        var fi = new FileInfo(path);
        // Valid model is at least 1 MB
        return fi.Length > 1 * 1024 * 1024;
    }

    public long GetModelSizeBytes(int factor = 2)
    {
        var path = GetModelPath(factor);
        if (!File.Exists(path)) return 0;
        return new FileInfo(path).Length;
    }

    public async Task<bool> DownloadModelAsync(int factor = 2, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var url = factor == 4 ? Model4xUrl : Model2xUrl;
        var targetPath = GetModelPath(factor);
        var tempPath = targetPath + ".download";

        try
        {
            AppLogger.Info("Upscale", $"Starte Download des {factor}x Super-Resolution Modells von {url}...");
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 67_000_000L;
            long totalRead = 0;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, ct);
                    totalRead += read;

                    double pct = Math.Min(100.0, (double)totalRead / totalBytes * 100.0);
                    progress?.Report(pct);
                }
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);

            AppLogger.Info("Upscale", $"{factor}x Modell erfolgreich heruntergeladen ({totalRead} Bytes): {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Upscale", $"Fehler beim Herunterladen des {factor}x KI-Modells", ex);
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
            return false;
        }
    }

    public bool DeleteModel(int factor = 2)
    {
        try
        {
            var path = GetModelPath(factor);
            if (File.Exists(path))
            {
                File.Delete(path);
                AppLogger.Info("Upscale", $"{factor}x Super-Resolution Modell gelöscht.");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Upscale", "Fehler beim Löschen des KI-Modells", ex);
            return false;
        }
    }

    public async Task<SKBitmap> UpscaleAsync(
        SKBitmap source,
        int factor = 2,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (factor != 2 && factor != 4)
        {
            factor = 2;
        }

        // If AI model is present, run ONNX inference
        if (IsAiModelDownloaded(factor))
        {
            try
            {
                return await Task.Run(() => RunOnnxUpscale(source, factor), ct);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Upscale", $"ONNX-Inferenz fehlgeschlagen ({ex.Message}). Wechsle zu High-Quality Lanczos/Detail Fallback.", ex);
            }
        }

        // High-Quality SkiaSharp Lanczos + Detail Sharpening Fallback
        return await Task.Run(() => UpscaleHighQuality(source, factor), ct);
    }

    public SKBitmap UpscaleHighQuality(SKBitmap source, int factor = 2)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (factor < 1) factor = 2;

        int targetWidth = source.Width * factor;
        int targetHeight = source.Height * factor;

        // 1. High-Quality Lanczos3 Resampling
        var upscaled = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

        using (var canvas = new SKCanvas(upscaled))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            };
            canvas.DrawBitmap(source, new SKRect(0, 0, targetWidth, targetHeight), paint);
            canvas.Flush();
        }

        // 2. High-Frequency Detail Extraction & Edge-Preserving Sharpening
        // Render a Gaussian blurred copy of the upscaled image
        using var blurred = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(blurred))
        {
            using var blurPaint = new SKPaint
            {
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(1.1f * factor, 1.1f * factor)
            };
            canvas.DrawBitmap(upscaled, 0, 0, blurPaint);
            canvas.Flush();
        }

        // 3. Pixel-level Unsharp Masking & Detail Gain
        var sharpened = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        var origSpan = upscaled.GetPixelSpan();
        var blurSpan = blurred.GetPixelSpan();

        unsafe
        {
            byte* dst = (byte*)sharpened.GetPixels().ToPointer();
            int length = origSpan.Length;
            float gain = 1.35f;

            for (int i = 0; i < length; i += 4)
            {
                // BGRA format
                int bOrig = origSpan[i];
                int gOrig = origSpan[i + 1];
                int rOrig = origSpan[i + 2];
                byte a = origSpan[i + 3];

                int bBlur = blurSpan[i];
                int gBlur = blurSpan[i + 1];
                int rBlur = blurSpan[i + 2];

                int deltaB = bOrig - bBlur;
                int deltaG = gOrig - gBlur;
                int deltaR = rOrig - rBlur;

                // Small threshold of 2 to suppress noise/grain in flat areas
                int bSharp = Math.Abs(deltaB) > 2 ? Math.Clamp((int)(bOrig + gain * deltaB), 0, 255) : bOrig;
                int gSharp = Math.Abs(deltaG) > 2 ? Math.Clamp((int)(gOrig + gain * deltaG), 0, 255) : gOrig;
                int rSharp = Math.Abs(deltaR) > 2 ? Math.Clamp((int)(rOrig + gain * deltaR), 0, 255) : rOrig;

                dst[i] = (byte)bSharp;
                dst[i + 1] = (byte)gSharp;
                dst[i + 2] = (byte)rSharp;
                dst[i + 3] = a;
            }
        }

        upscaled.Dispose();
        return sharpened;
    }

    private SKBitmap RunOnnxUpscale(SKBitmap source, int factor)
    {
        var modelPath = GetModelPath(factor);
        using var session = new InferenceSession(modelPath);

        string inputName = session.InputMetadata.Keys.First();
        int width = source.Width;
        int height = source.Height;

        // Tensor [1, 3, height, width] with values in [0, 1]
        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

        // Ensure 32-bit BGRA format for direct span access
        bool needsDispose = source.ColorType != SKColorType.Bgra8888;
        var normalized = needsDispose
            ? source.Copy(SKColorType.Bgra8888)
            : source;

        try
        {
            var span = normalized.GetPixelSpan();
            int rowBytes = normalized.RowBytes;

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * rowBytes;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowStart + (x * 4);
                    byte b = span[idx];
                    byte g = span[idx + 1];
                    byte r = span[idx + 2];

                    tensor[0, 0, y, x] = r / 255.0f;
                    tensor[0, 1, y, x] = g / 255.0f;
                    tensor[0, 2, y, x] = b / 255.0f;
                }
            }
        }
        finally
        {
            if (needsDispose)
            {
                normalized.Dispose();
            }
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, tensor)
        };

        using var results = session.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();

        int outHeight = outputTensor.Dimensions[2];
        int outWidth = outputTensor.Dimensions[3];

        var result = new SKBitmap(outWidth, outHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        unsafe
        {
            byte* dst = (byte*)result.GetPixels().ToPointer();
            int resRowBytes = result.RowBytes;

            for (int y = 0; y < outHeight; y++)
            {
                int rowStart = y * resRowBytes;
                for (int x = 0; x < outWidth; x++)
                {
                    byte r = (byte)Math.Clamp((int)(outputTensor[0, 0, y, x] * 255.0f), 0, 255);
                    byte g = (byte)Math.Clamp((int)(outputTensor[0, 1, y, x] * 255.0f), 0, 255);
                    byte b = (byte)Math.Clamp((int)(outputTensor[0, 2, y, x] * 255.0f), 0, 255);

                    int idx = rowStart + (x * 4);
                    dst[idx] = b;
                    dst[idx + 1] = g;
                    dst[idx + 2] = r;
                    dst[idx + 3] = 255;
                }
            }
        }

        return result;
    }
}
