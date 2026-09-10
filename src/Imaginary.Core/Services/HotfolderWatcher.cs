using System.Collections.Concurrent;
using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public class HotfolderWatcher : IHotfolderWatcher
{
    private readonly IBatchProcessor _batchProcessor;
    private readonly IImageFormatDetector _formatDetector;
    private FileSystemWatcher? _watcher;
    private ConversionOptions? _options;
    private HotfolderOriginalAction _originalAction = HotfolderOriginalAction.Keep;
    private string _originalSubfolder = "Originale";
    private readonly ConcurrentDictionary<string, byte> _processingFiles = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;

    public bool IsRunning => _watcher != null && _watcher.EnableRaisingEvents;
    public string? CurrentWatchPath { get; private set; }
    public string? CurrentOutputPath { get; private set; }

    public event EventHandler<HotfolderFileEventArgs>? FileProcessed;
    public event EventHandler<string>? StatusMessage;

    public HotfolderWatcher(IBatchProcessor batchProcessor, IImageFormatDetector formatDetector)
    {
        _batchProcessor = batchProcessor ?? throw new ArgumentNullException(nameof(batchProcessor));
        _formatDetector = formatDetector ?? throw new ArgumentNullException(nameof(formatDetector));
    }

    public void Start(string watchPath, string outputPath, ConversionOptions options, bool includeSubdirectories = false, HotfolderOriginalAction originalAction = HotfolderOriginalAction.Keep, string originalSubfolder = "Originale")
    {
        Stop();

        if (!Directory.Exists(watchPath))
        {
            Directory.CreateDirectory(watchPath);
        }
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        CurrentWatchPath = watchPath;
        CurrentOutputPath = outputPath;
        _options = options;
        _originalAction = originalAction;
        _originalSubfolder = string.IsNullOrWhiteSpace(originalSubfolder) ? "Originale" : originalSubfolder;
        _cts = new CancellationTokenSource();

        _watcher = new FileSystemWatcher(watchPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = includeSubdirectories,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;
        _watcher.Renamed += OnFileRenamed;

        StatusMessage?.Invoke(this, $"Hotfolder gestartet: Überwache '{watchPath}' -> Ziel '{outputPath}'");

        // Scan for existing files already placed in folder
        _ = Task.Run(() => ScanExistingFiles(watchPath, includeSubdirectories, _cts.Token));
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileCreated;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Dispose();
            _watcher = null;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _processingFiles.Clear();

        StatusMessage?.Invoke(this, "Hotfolder angehalten.");
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        QueueFileProcessing(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        QueueFileProcessing(e.FullPath);
    }

    private bool ShouldIgnorePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(CurrentWatchPath) || string.IsNullOrWhiteSpace(filePath)) return true;

        // Ignore files in target output path
        if (!string.IsNullOrWhiteSpace(CurrentOutputPath) && filePath.StartsWith(CurrentOutputPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Ignore files inside originalSubfolder
        if (_originalAction == HotfolderOriginalAction.MoveToSubfolder)
        {
            var originalDir = Path.Combine(CurrentWatchPath, _originalSubfolder);
            if (filePath.StartsWith(originalDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ScanExistingFiles(string directory, bool includeSubdirs, CancellationToken ct)
    {
        try
        {
            var option = includeSubdirs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(directory, "*.*", option);
            foreach (var f in files)
            {
                if (ct.IsCancellationRequested) break;
                if (!ShouldIgnorePath(f))
                {
                    QueueFileProcessing(f);
                }
            }
        }
        catch
        {
            // Ignore scan errors
        }
    }

    private void QueueFileProcessing(string filePath)
    {
        if (ShouldIgnorePath(filePath))
        {
            return;
        }

        if (!_processingFiles.TryAdd(filePath, 0))
        {
            return; // Already being processed
        }

        var fmt = _formatDetector.DetectFromExtension(filePath);
        if (fmt == ImageFormat.Unknown)
        {
            _processingFiles.TryRemove(filePath, out _);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var ready = await WaitForFileReadyAsync(filePath, maxAttempts: 20, delayMs: 400);
                if (!ready)
                {
                    StatusMessage?.Invoke(this, $"Datei '{Path.GetFileName(filePath)}' gesperrt; übersprungen.");
                    return;
                }

                var jobOptions = _options ?? new ConversionOptions();
                jobOptions.OutputDirectory = CurrentOutputPath;

                var input = ImageJobInput.FromFile(filePath);
                var result = await _batchProcessor.ProcessSingleAsync(input, jobOptions, saveToDisk: true);

                FileProcessed?.Invoke(this, new HotfolderFileEventArgs
                {
                    SourceFile = filePath,
                    Result = result
                });

                if (result.Success)
                {
                    StatusMessage?.Invoke(this, $"Konvertiert: {result.FileName} -> {Path.GetFileName(result.TargetPath)}");

                    // Nachbehandlung des Originals
                    try
                    {
                        if (_originalAction == HotfolderOriginalAction.MoveToSubfolder && !string.IsNullOrWhiteSpace(CurrentWatchPath))
                        {
                            var targetDir = Path.Combine(CurrentWatchPath, _originalSubfolder);
                            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                            var destFile = Path.Combine(targetDir, Path.GetFileName(filePath));
                            if (File.Exists(destFile))
                            {
                                var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                                var ext = Path.GetExtension(filePath);
                                destFile = Path.Combine(targetDir, $"{nameWithoutExt}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
                            }
                            File.Move(filePath, destFile, overwrite: true);
                        }
                        else if (_originalAction == HotfolderOriginalAction.Delete)
                        {
                            File.Delete(filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusMessage?.Invoke(this, $"Original-Aktion fehlgeschlagen für {result.FileName}: {ex.Message}");
                    }
                }
                else
                {
                    StatusMessage?.Invoke(this, $"Fehler bei {result.FileName}: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Fehler bei {Path.GetFileName(filePath)}: {ex.Message}");
            }
            finally
            {
                _processingFiles.TryRemove(filePath, out _);
            }
        });
    }

    private static async Task<bool> WaitForFileReadyAsync(string filePath, int maxAttempts, int delayMs)
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                if (fs.Length > 0)
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // File still being written
            }
            catch (UnauthorizedAccessException)
            {
                // File permission issue
            }

            await Task.Delay(delayMs);
        }

        return false;
    }

    public void Dispose()
    {
        Stop();
    }
}
