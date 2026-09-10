namespace Imaginary.Core.Models;

public class ImageJobResult
{
    public bool Success { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? SourcePath { get; set; }
    public string? TargetPath { get; set; }
    public byte[]? OutputData { get; set; }
    public long OriginalSizeBytes { get; set; }
    public long FinalSizeBytes { get; set; }
    public ImageDimensions? OriginalDimensions { get; set; }
    public ImageDimensions? FinalDimensions { get; set; }
    public ImageFormat OriginalFormat { get; set; }
    public ImageFormat FinalFormat { get; set; }
    public string? AppliedStrategy { get; set; }
    public List<string> Warnings { get; } = new();
    public string? ErrorMessage { get; set; }

    public double SavingsPercentage => OriginalSizeBytes > 0 && FinalSizeBytes > 0
        ? Math.Max(0, (1.0 - ((double)FinalSizeBytes / OriginalSizeBytes)) * 100.0)
        : 0.0;
}
