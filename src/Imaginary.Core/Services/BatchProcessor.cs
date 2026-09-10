using System.Diagnostics;
using Imaginary.Core.Logging;
using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class BatchProcessor : IBatchProcessor
{
    private readonly IImageFormatDetector _formatDetector;
    private readonly IImageConverter _converter;
    private readonly IImageResizer _resizer;
    private readonly ISizeConstraintSolver _solver;
    private readonly IOutputPathResolver _pathResolver;
    private readonly IWatermarkService _watermarkService;

    public BatchProcessor(
        IImageFormatDetector formatDetector,
        IImageConverter converter,
        IImageResizer resizer,
        ISizeConstraintSolver solver,
        IOutputPathResolver pathResolver,
        IWatermarkService watermarkService)
    {
        _formatDetector = formatDetector ?? throw new ArgumentNullException(nameof(formatDetector));
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _resizer = resizer ?? throw new ArgumentNullException(nameof(resizer));
        _solver = solver ?? throw new ArgumentNullException(nameof(solver));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _watermarkService = watermarkService ?? throw new ArgumentNullException(nameof(watermarkService));
    }

    public async Task<ImageJobResult> ProcessSingleAsync(
        ImageJobInput input,
        ConversionOptions options,
        bool saveToDisk = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sw = Stopwatch.StartNew();
        var result = new ImageJobResult
        {
            FileName = input.FileName,
            SourcePath = input.SourcePath
        };

        try
        {
            AppLogger.Debug("BatchProcessor", $"Verarbeite Datei: {input.FileName} (Pfad: {input.SourcePath ?? "Stream/Bytes"})");

            // 1. Obtain input bytes
            byte[] inputBytes;
            if (input.Data != null)
            {
                inputBytes = input.Data;
            }
            else if (input.Stream != null)
            {
                using var ms = new MemoryStream();
                if (input.Stream.CanSeek)
                {
                    input.Stream.Position = 0;
                }
                await input.Stream.CopyToAsync(ms, cancellationToken);
                inputBytes = ms.ToArray();
            }
            else if (!string.IsNullOrWhiteSpace(input.SourcePath) && File.Exists(input.SourcePath))
            {
                inputBytes = await File.ReadAllBytesAsync(input.SourcePath, cancellationToken);
            }
            else
            {
                throw new FileNotFoundException($"Quelldatei nicht gefunden: {input.SourcePath ?? input.FileName}");
            }

            result.OriginalSizeBytes = inputBytes.Length;

            // 2. Detect format
            var detectedFormat = _formatDetector.DetectFormat(inputBytes);
            if (detectedFormat == ImageFormat.Unknown)
            {
                detectedFormat = _formatDetector.DetectFromExtension(input.FileName);
            }
            result.OriginalFormat = detectedFormat;

            // 3. Determine target format
            var targetFormat = options.TargetFormat.HasValue && options.TargetFormat.Value != ImageFormat.Unknown
                ? options.TargetFormat.Value
                : (detectedFormat != ImageFormat.Unknown ? detectedFormat : ImageFormat.Jpeg);
            result.FinalFormat = targetFormat;

            // 4. Decode bitmap with EXIF orientation
            var (sourceBitmap, _) = _converter.LoadBitmap(inputBytes);
            using (sourceBitmap)
            {
                result.OriginalDimensions = new ImageDimensions(sourceBitmap.Width, sourceBitmap.Height);
                AppLogger.Debug("BatchProcessor", $"[{input.FileName}] Dekodiert: {sourceBitmap.Width}x{sourceBitmap.Height} ({detectedFormat}) -> Ziel: {targetFormat}");

                // 5. Apply Resize if requested
                using var resizedBitmap = _resizer.Resize(sourceBitmap, options);
                if (resizedBitmap.Width != sourceBitmap.Width || resizedBitmap.Height != sourceBitmap.Height)
                {
                    AppLogger.Debug("BatchProcessor", $"[{input.FileName}] Skaliert auf {resizedBitmap.Width}x{resizedBitmap.Height}");
                }

                // 5b. Apply Watermark if requested
                if (options.Watermark != null && options.Watermark.Type != WatermarkType.None)
                {
                    _watermarkService.ApplyWatermark(resizedBitmap, options.Watermark);
                    AppLogger.Debug("BatchProcessor", $"[{input.FileName}] Wasserzeichen angewendet ({options.Watermark.Type})");
                }

                // 6. Apply SizeConstraintSolver (quality adjustments, fallbacks, quantization)
                var solved = _solver.Solve(resizedBitmap, targetFormat, options);

                result.FinalDimensions = solved.FinalDimensions;
                result.OutputData = solved.EncodedData;
                result.FinalSizeBytes = solved.EncodedData.Length;
                result.AppliedStrategy = solved.AppliedStrategy;
                result.Warnings.AddRange(solved.Warnings);

                // 7. Save to disk if requested
                if (saveToDisk)
                {
                    var targetPath = _pathResolver.ResolveOutputPath(
                        input.SourcePath,
                        input.FileName,
                        targetFormat,
                        options.OutputDirectory);

                    await File.WriteAllBytesAsync(targetPath, solved.EncodedData, cancellationToken);
                    result.TargetPath = targetPath;
                }

                result.Success = true;
                sw.Stop();

                var origKb = result.OriginalSizeBytes / 1024.0;
                var finalKb = result.FinalSizeBytes / 1024.0;
                var savings = result.SavingsPercentage;
                AppLogger.Info("BatchProcessor",
                    $"[{input.FileName}] Erfolgreich konvertiert: {origKb:N1} KB -> {finalKb:N1} KB ({savings:N1}% Ersparnis) in {sw.ElapsedMilliseconds} ms. Ziel: {result.TargetPath ?? "Memory"}");
            }
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warn("BatchProcessor", $"[{input.FileName}] Konvertierung vom Benutzer abgebrochen.");
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Success = false;
            result.ErrorMessage = ex.Message;
            AppLogger.Error("BatchProcessor", $"[{input.FileName}] FEHLER bei Konvertierung: {ex.Message}", ex);
        }

        return result;
    }

    public async Task<BatchResult> ProcessBatchAsync(
        IReadOnlyList<ImageJobInput> inputs,
        ConversionOptions options,
        bool saveToDisk = true,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var batchResult = new BatchResult();
        var completedCount = 0;
        var totalCount = inputs.Count;

        AppLogger.Info("BatchProcessor",
            $"Starte Stapelverarbeitung für {totalCount} Datei(en). " +
            $"Zielformat: {options.TargetFormat?.ToString() ?? "Auto"}, " +
            $"Qualität: {options.DefaultQuality}, " +
            $"Größenbegrenzung: {(options.MaxFileSizeInBytes.HasValue ? $"{options.MaxFileSizeInBytes / 1024.0:N0} KB" : "Keine")}, " +
            $"Metadaten entfernen: {options.StripMetadata}, " +
            $"Wasserzeichen: {options.Watermark?.Type.ToString() ?? "Keins"}");

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
            CancellationToken = cancellationToken
        };

        var jobResults = new ImageJobResult[totalCount];

        try
        {
            await Parallel.ForEachAsync(
                inputs.Select((input, index) => (input, index)),
                parallelOptions,
                async (item, ct) =>
                {
                    var singleResult = await ProcessSingleAsync(item.input, options, saveToDisk, ct);
                    jobResults[item.index] = singleResult;

                    var current = Interlocked.Increment(ref completedCount);
                    progress?.Report(new BatchProgress
                    {
                        CurrentIndex = current,
                        TotalCount = totalCount,
                        CurrentFileName = item.input.FileName,
                        LatestResult = singleResult
                    });
                });
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warn("BatchProcessor", "Stapelverarbeitung wurde abgebrochen.");
            throw;
        }

        batchResult.Results.AddRange(jobResults.Where(r => r != null));
        sw.Stop();
        batchResult.Duration = sw.Elapsed;

        var savedMb = (batchResult.TotalOriginalSizeBytes - batchResult.TotalFinalSizeBytes) / (1024.0 * 1024.0);
        AppLogger.Info("BatchProcessor",
            $"Stapelverarbeitung beendet in {sw.Elapsed.TotalSeconds:N1}s: " +
            $"{batchResult.SucceededCount} erfolgreich, {batchResult.FailedCount} fehlgeschlagen. " +
            $"Gesamtersparnis: {savedMb:N2} MB ({batchResult.OverallSavingsPercentage:N1}%).");

        return batchResult;
    }
}
