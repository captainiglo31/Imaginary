namespace Imaginary.Core.Models;

public class ImageHistogramData
{
    public int[] Red { get; set; } = new int[256];
    public int[] Green { get; set; } = new int[256];
    public int[] Blue { get; set; } = new int[256];
    public int[] Luminance { get; set; } = new int[256];

    public int MaxValue { get; set; }
    public int ShadowClippingCount { get; set; }
    public int HighlightClippingCount { get; set; }
    public double ShadowClippingPercentage { get; set; }
    public double HighlightClippingPercentage { get; set; }
}
