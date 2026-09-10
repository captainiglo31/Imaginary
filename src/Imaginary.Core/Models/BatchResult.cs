namespace Imaginary.Core.Models;

public class BatchResult
{
    public List<ImageJobResult> Results { get; } = new();
    public int TotalCount => Results.Count;
    public int SucceededCount => Results.Count(r => r.Success);
    public int FailedCount => Results.Count(r => !r.Success);
    public int WarningCount => Results.Count(r => r.Warnings.Count > 0);
    public long TotalOriginalSizeBytes => Results.Sum(r => r.OriginalSizeBytes);
    public long TotalFinalSizeBytes => Results.Sum(r => r.FinalSizeBytes);
    public TimeSpan Duration { get; set; }

    public double OverallSavingsPercentage => TotalOriginalSizeBytes > 0 && TotalFinalSizeBytes > 0
        ? Math.Max(0, (1.0 - ((double)TotalFinalSizeBytes / TotalOriginalSizeBytes)) * 100.0)
        : 0.0;
}
