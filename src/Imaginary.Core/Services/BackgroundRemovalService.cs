using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Imaginary.Core.Logging;
using Imaginary.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class BackgroundRemovalService : IBackgroundRemovalService
{
    private const string ModelFileName = "u2netp.onnx";
    private const string ModelDownloadUrl = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx";

    private readonly IImageEditorService _editorService;
    private readonly HttpClient _httpClient;

    public BackgroundRemovalService(IImageEditorService editorService, HttpClient? httpClient = null)
    {
        _editorService = editorService ?? throw new ArgumentNullException(nameof(editorService));
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public string GetModelPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Imaginary",
            "Models");

        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return Path.Combine(dir, ModelFileName);
    }

    public bool IsAiModelDownloaded()
    {
        var path = GetModelPath();
        if (!File.Exists(path)) return false;

        // Ensure file is at least 4 MB (valid model)
        var fi = new FileInfo(path);
        return fi.Length > 4 * 1024 * 1024;
    }

    public long GetModelSizeBytes()
    {
        var path = GetModelPath();
        if (!File.Exists(path)) return 0;
        return new FileInfo(path).Length;
    }

    public async Task<bool> DownloadModelAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var targetPath = GetModelPath();
        var tempPath = targetPath + ".download";

        try
        {
            AppLogger.Info("AI", $"Starte Download des U-2-Netp Modells von {ModelDownloadUrl}...");
            using var response = await _httpClient.GetAsync(ModelDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? (4_700_000);
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

            AppLogger.Info("AI", $"U-2-Netp Modell erfolgreich heruntergeladen ({totalRead} Bytes): {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("AI", "Fehler beim Herunterladen des KI-Modells", ex);
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
            return false;
        }
    }

    public bool DeleteModel()
    {
        try
        {
            var path = GetModelPath();
            if (File.Exists(path))
            {
                File.Delete(path);
                AppLogger.Info("AI", "U-2-Netp Modell gelöscht.");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error("AI", "Fehler beim Löschen des KI-Modells", ex);
            return false;
        }
    }

    public async Task<SKBitmap> RemoveBackgroundAsync(
        SKBitmap source,
        BackgroundRemovalMode mode,
        float tolerance = 0.15f,
        SKColor? keyColor = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (mode == BackgroundRemovalMode.ColorKey || !IsAiModelDownloaded())
        {
            // Mode A: Color Keying / Magic Wand
            var color = keyColor ?? DetectDominantCornerColor(source);
            return await Task.Run(() => _editorService.RemoveBackgroundByColor(source, color, tolerance), ct);
        }

        // Mode B: AI ONNX Inference
        return await Task.Run(() => RunOnnxInference(source), ct);
    }

    private SKBitmap RunOnnxInference(SKBitmap source)
    {
        var modelPath = GetModelPath();
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("KI-Modell wurde noch nicht heruntergeladen.", modelPath);
        }

        using var session = new InferenceSession(modelPath);
        string inputName = session.InputMetadata.Keys.First();

        const int targetSize = 320;
        using var resized = source.Resize(new SKImageInfo(targetSize, targetSize, SKColorType.Rgba8888), SKFilterQuality.Medium);
        if (resized == null) throw new InvalidOperationException("Bild konnte für KI-Inferenz nicht skaliert werden.");

        // Tensor: [1, 3, 320, 320] ImageNet normalization
        var tensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });
        float[] mean = { 0.485f, 0.456f, 0.406f };
        float[] std = { 0.229f, 0.224f, 0.225f };

        for (int y = 0; y < targetSize; y++)
        {
            for (int x = 0; x < targetSize; x++)
            {
                var pixel = resized.GetPixel(x, y);
                tensor[0, 0, y, x] = (pixel.Red / 255.0f - mean[0]) / std[0];
                tensor[0, 1, y, x] = (pixel.Green / 255.0f - mean[1]) / std[1];
                tensor[0, 2, y, x] = (pixel.Blue / 255.0f - mean[2]) / std[2];
            }
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, tensor)
        };

        using var results = session.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();

        // Find min and max for normalization
        float minVal = float.MaxValue;
        float maxVal = float.MinValue;
        for (int y = 0; y < targetSize; y++)
        {
            for (int x = 0; x < targetSize; x++)
            {
                float val = outputTensor[0, 0, y, x];
                if (val < minVal) minVal = val;
                if (val > maxVal) maxVal = val;
            }
        }

        float range = Math.Max(1e-6f, maxVal - minVal);

        // Build 320x320 Alpha Mask
        using var mask320 = new SKBitmap(targetSize, targetSize, SKColorType.Gray8, SKAlphaType.Opaque);
        for (int y = 0; y < targetSize; y++)
        {
            for (int x = 0; x < targetSize; x++)
            {
                float normalized = (outputTensor[0, 0, y, x] - minVal) / range;
                byte alpha = (byte)Math.Clamp((int)(normalized * 255.0f), 0, 255);
                mask320.SetPixel(x, y, new SKColor(alpha, alpha, alpha));
            }
        }

        // Upscale mask to original dimensions
        using var fullMask = mask320.Resize(new SKImageInfo(source.Width, source.Height, SKColorType.Gray8), SKFilterQuality.High);

        // Apply alpha mask to result
        var finalBitmap = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var srcPx = source.GetPixel(x, y);
                var maskPx = fullMask.GetPixel(x, y);
                byte alpha = (byte)((srcPx.Alpha * maskPx.Red) / 255);
                finalBitmap.SetPixel(x, y, new SKColor(srcPx.Red, srcPx.Green, srcPx.Blue, alpha));
            }
        }

        return finalBitmap;
    }

    public async Task<SKBitmap> SegmentObjectRegionAsync(
        SKBitmap source,
        SKRectI boundingBox,
        IReadOnlyList<SKPoint>? brushPoints = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!IsAiModelDownloaded())
        {
            throw new InvalidOperationException("Das KI-Modell wurde noch nicht heruntergeladen.");
        }

        return await Task.Run(() =>
        {
            // Clamp and expand bounding box slightly (15% padding) for context
            int padX = (int)(boundingBox.Width * 0.15f);
            int padY = (int)(boundingBox.Height * 0.15f);

            int left = Math.Clamp(boundingBox.Left - padX, 0, source.Width);
            int top = Math.Clamp(boundingBox.Top - padY, 0, source.Height);
            int right = Math.Clamp(boundingBox.Right + padX, 0, source.Width);
            int bottom = Math.Clamp(boundingBox.Bottom + padY, 0, source.Height);

            int regionW = right - left;
            int regionH = bottom - top;
            if (regionW < 10 || regionH < 10)
            {
                // Fallback to full inference if bounding box is too small
                return RunOnnxInference(source);
            }

            var regionRect = new SKRectI(left, top, right, bottom);
            using var subBitmap = _editorService.Crop(source, regionRect);

            // Run ONNX inference on cropped subregion
            using var subSegmented = RunOnnxInference(subBitmap);

            // Compose: create transparent full-size bitmap, and paste the subregion
            var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            result.Erase(SKColors.Transparent);

            using (var canvas = new SKCanvas(result))
            {
                canvas.DrawBitmap(subSegmented, left, top);
                canvas.Flush();
            }

            return result;
        }, ct);
    }

    public SKBitmap ApplyMaskBrush(
        SKBitmap current,
        SKBitmap original,
        SKPoint point,
        float radius,
        bool restore)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(original);

        int minX = Math.Clamp((int)(point.X - radius), 0, current.Width - 1);
        int maxX = Math.Clamp((int)(point.X + radius), 0, current.Width - 1);
        int minY = Math.Clamp((int)(point.Y - radius), 0, current.Height - 1);
        int maxY = Math.Clamp((int)(point.Y + radius), 0, current.Height - 1);

        float rSquared = radius * radius;
        float innerRadius = radius * 0.65f;

        for (int y = minY; y <= maxY; y++)
        {
            float dy = y - point.Y;
            float dy2 = dy * dy;

            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - point.X;
                float dist2 = dx * dx + dy2;

                if (dist2 <= rSquared)
                {
                    float factor = 1.0f;
                    if (dist2 > innerRadius * innerRadius)
                    {
                        float dist = MathF.Sqrt(dist2);
                        factor = 1.0f - ((dist - innerRadius) / (radius - innerRadius));
                    }

                    var curPx = current.GetPixel(x, y);
                    int origX = Math.Clamp(x, 0, original.Width - 1);
                    int origY = Math.Clamp(y, 0, original.Height - 1);
                    var origPx = original.GetPixel(origX, origY);

                    if (restore)
                    {
                        byte newAlpha = (byte)Math.Clamp((int)(curPx.Alpha + (origPx.Alpha - curPx.Alpha) * factor), 0, 255);
                        current.SetPixel(x, y, new SKColor(origPx.Red, origPx.Green, origPx.Blue, newAlpha));
                    }
                    else
                    {
                        byte newAlpha = (byte)Math.Clamp((int)(curPx.Alpha * (1.0f - factor)), 0, 255);
                        current.SetPixel(x, y, new SKColor(curPx.Red, curPx.Green, curPx.Blue, newAlpha));
                    }
                }
            }
        }

        return current;
    }

    private static SKColor DetectDominantCornerColor(SKBitmap bitmap)
    {
        // Sample top-left corner
        return bitmap.GetPixel(0, 0);
    }
}
