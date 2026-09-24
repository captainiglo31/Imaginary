namespace Imaginary.Core.Models;

public enum SocialMediaFitMode
{
    FillCrop,
    Pad,
    PadBlur
}

public class SocialMediaTemplate
{
    public string Id { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public string AspectRatioText { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Icon { get; init; } = "📱";

    public string DisplayTitle => $"{Icon} {Platform}: {Name} ({Width}×{Height})";

    public double AspectRatio => (double)Width / Math.Max(1, Height);
}
