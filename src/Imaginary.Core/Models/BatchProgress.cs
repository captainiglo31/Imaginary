namespace Imaginary.Core.Models;

public class BatchProgress
{
    public int CurrentIndex { get; set; }
    public int TotalCount { get; set; }
    public string CurrentFileName { get; set; } = string.Empty;
    public ImageJobResult? LatestResult { get; set; }
    public double Percentage => TotalCount > 0 ? ((double)CurrentIndex / TotalCount) * 100.0 : 0.0;
}
