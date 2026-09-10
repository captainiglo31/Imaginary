namespace Imaginary.Core.Models;

public class HotfolderHistoryItem
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string FileName { get; init; } = string.Empty;
    public string TargetFileName { get; init; } = string.Empty;
    public long OriginalSizeBytes { get; init; }
    public long FinalSizeBytes { get; init; }
    public double SavingsPercent { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
    public string FormattedOriginalSize => FormatBytes(OriginalSizeBytes);
    public string FormattedFinalSize => FormatBytes(FinalSizeBytes);
    public string FormattedSavings => SavingsPercent >= 0 ? $"-{SavingsPercent:F1} %" : $"+{-SavingsPercent:F1} %";
    public string StatusText => Success ? "Erfolgreich" : "Fehler";

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}
