using System;
using System.Threading;
using System.Threading.Tasks;
using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public interface IBackgroundRemovalService
{
    /// <summary>
    /// Checks if the local ONNX AI model (u2netp.onnx) has been downloaded.
    /// </summary>
    bool IsAiModelDownloaded();

    /// <summary>
    /// Returns the absolute path to the local ONNX model file.
    /// </summary>
    string GetModelPath();

    /// <summary>
    /// Returns the file size of the local ONNX model in bytes, or 0 if not downloaded.
    /// </summary>
    long GetModelSizeBytes();

    /// <summary>
    /// Downloads the lightweight ONNX background removal model (~4.7 MB) with progress reporting.
    /// </summary>
    Task<bool> DownloadModelAsync(IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Removes the downloaded ONNX model from disk.
    /// </summary>
    bool DeleteModel();

    /// <summary>
    /// Removes the background using either ColorKey (Magic Wand) or the local AI ONNX model.
    /// </summary>
    Task<SKBitmap> RemoveBackgroundAsync(
        SKBitmap source,
        BackgroundRemovalMode mode,
        float tolerance = 0.15f,
        SKColor? keyColor = null,
        CancellationToken ct = default);
}
