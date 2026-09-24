using SkiaSharp;

namespace Imaginary.Core.Services;

public interface IUpscaleService
{
    string GetModelPath(int factor = 2);
    bool IsAiModelDownloaded(int factor = 2);
    long GetModelSizeBytes(int factor = 2);
    Task<bool> DownloadModelAsync(int factor = 2, IProgress<double>? progress = null, CancellationToken ct = default);
    bool DeleteModel(int factor = 2);

    Task<SKBitmap> UpscaleAsync(
        SKBitmap source,
        int factor = 2,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    SKBitmap UpscaleHighQuality(SKBitmap source, int factor = 2);
}
