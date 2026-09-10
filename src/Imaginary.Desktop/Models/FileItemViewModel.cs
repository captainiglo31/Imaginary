using CommunityToolkit.Mvvm.ComponentModel;
using Imaginary.Core.Models;

namespace Imaginary.Desktop.Models;

public enum JobStatus
{
    Pending,
    Processing,
    Done,
    Failed,
    Skipped
}

public partial class FileItemViewModel : ObservableObject
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long OriginalSizeBytes { get; set; }

    [ObservableProperty]
    private JobStatus _status = JobStatus.Pending;

    [ObservableProperty]
    private string _statusMessage = "Ausstehend";

    [ObservableProperty]
    private long _finalSizeBytes;

    [ObservableProperty]
    private ImageDimensions? _originalDimensions;

    [ObservableProperty]
    private ImageDimensions? _finalDimensions;

    [ObservableProperty]
    private double _savingsPercentage;

    [ObservableProperty]
    private string? _appliedStrategy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _targetPath;

    public List<string> Warnings { get; } = new();

    public string OriginalSizeFormatted => FormatBytes(OriginalSizeBytes);
    public string FinalSizeFormatted => FinalSizeBytes > 0 ? FormatBytes(FinalSizeBytes) : "-";
    public string SavingsFormatted => SavingsPercentage > 0 ? $"-{SavingsPercentage:N1} %" : (FinalSizeBytes > 0 ? "0 %" : "-");

    public string OriginalDimensionsFormatted => OriginalDimensions.HasValue ? OriginalDimensions.Value.ToString() : "-";
    public string FinalDimensionsFormatted => FinalDimensions.HasValue ? FinalDimensions.Value.ToString() : "-";

    public bool HasWarnings => Warnings.Count > 0;
    public string WarningsFormatted => string.Join("; ", Warnings);

    public void UpdateFromJobResult(ImageJobResult result)
    {
        if (result.Success)
        {
            Status = JobStatus.Done;
            FinalSizeBytes = result.FinalSizeBytes;
            OriginalDimensions = result.OriginalDimensions;
            FinalDimensions = result.FinalDimensions;
            SavingsPercentage = result.SavingsPercentage;
            AppliedStrategy = result.AppliedStrategy;
            TargetPath = result.TargetPath;
            Warnings.Clear();
            Warnings.AddRange(result.Warnings);

            OnPropertyChanged(nameof(FinalSizeFormatted));
            OnPropertyChanged(nameof(SavingsFormatted));
            OnPropertyChanged(nameof(FinalDimensionsFormatted));
            OnPropertyChanged(nameof(OriginalDimensionsFormatted));
            OnPropertyChanged(nameof(HasWarnings));
            OnPropertyChanged(nameof(WarningsFormatted));

            StatusMessage = HasWarnings ? "Fertig mit Warnung" : "Fertig";
        }
        else
        {
            Status = JobStatus.Failed;
            ErrorMessage = result.ErrorMessage;
            StatusMessage = $"Fehler: {result.ErrorMessage}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:N1} KB";
        return $"{bytes / (1024.0 * 1024.0):N2} MB";
    }
}
