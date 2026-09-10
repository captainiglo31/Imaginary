using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public class HotfolderFileEventArgs : EventArgs
{
    public string SourceFile { get; init; } = string.Empty;
    public ImageJobResult Result { get; init; } = new();
}

public interface IHotfolderWatcher : IDisposable
{
    bool IsRunning { get; }
    string? CurrentWatchPath { get; }
    string? CurrentOutputPath { get; }

    event EventHandler<HotfolderFileEventArgs>? FileProcessed;
    event EventHandler<string>? StatusMessage;

    void Start(string watchPath, string outputPath, ConversionOptions options, bool includeSubdirectories = false, HotfolderOriginalAction originalAction = HotfolderOriginalAction.Keep, string originalSubfolder = "Originale");
    void Stop();
}
