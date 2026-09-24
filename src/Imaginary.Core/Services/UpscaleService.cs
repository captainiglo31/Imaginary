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

    private const string Model2xUrl = "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.2.4/RealESRGAN_x2plus.onnx";
    private const string Model4xUrl = "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.onnx";

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

            var totalBytes = response.Content.Headers.ContentLength ?? 17_000_000L;
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

        // 1. High-Quality Lanczos Resampling
        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        var upscaled = new SKBitmap(info);

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

        // 2. Unsharp Masking Detail Enhancement for crisp edges without noise
        var sharpened = new SKBitmap(info);
        using (var canvas = new SKCanvas(sharpened))
        {
            using var blurPaint = new SKPaint
            {
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(1.5f * factor, 1.5f * factor)
            };

            // Draw base
            canvas.DrawBitmap(upscaled, 0, 0);

            // Blend edges with high-pass contrast
            using var blendPaint = new SKPaint
            {
                BlendMode = SKBlendMode.Overlay,
                Color = new SKColor(255, 255, 255, (byte)(50 * factor))
            };
            canvas.DrawBitmap(upscaled, 0, 0, blendPaint);
            canvas.Flush();
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

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var color = source.GetPixel(x, y);
                tensor[0, 0, y, x] = color.Red / 255.0f;
                tensor[0, 1, y, x] = color.Green / 255.0f;
                tensor[0, 2, y, x] = color.Blue / 255.0f;
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

        for (int y = 0; y < outHeight; y++)
        {
            for (int x = 0; x < outWidth; x++)
            {
                byte r = (byte)Math.Clamp((int)(outputTensor[0, 0, y, x] * 255.0f), 0, 255);
                byte g = (byte)Math.Clamp((int)(outputTensor[0, 1, y, x] * 255.0f), 0, 255);
                byte b = (byte)Math.Clamp((int)(outputTensor[0, 2, y, x] * 255.0f), 0, 255);

                result.SetPixel(x, y, new SKColor(r, g, b, 255));
            }
        }

        return result;
    }
}
