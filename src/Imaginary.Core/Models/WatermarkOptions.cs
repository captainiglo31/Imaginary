namespace Imaginary.Core.Models;

public enum WatermarkType
{
    None = 0,
    Text,
    Image
}

public enum WatermarkPosition
{
    BottomRight = 0,
    BottomLeft,
    BottomCenter,
    TopRight,
    TopLeft,
    TopCenter,
    Center
}

public class WatermarkOptions
{
    public bool Enabled { get; set; } = false;
    public WatermarkType Type { get; set; } = WatermarkType.None;
    public WatermarkPosition Position { get; set; } = WatermarkPosition.BottomRight;

    // Text watermark properties
    public string Text { get; set; } = "© Imaginary";
    public string FontFamily { get; set; } = "Arial";
    public float FontSize { get; set; } = 36f;
    public string ColorHex { get; set; } = "#FFFFFF";
    public string TextColor { get => ColorHex; set => ColorHex = value; }
    public float Opacity { get; set; } = 0.65f; // 0.0 to 1.0
    public bool IncludeShadow { get; set; } = true;

    // Image watermark properties
    public string? ImagePath { get; set; }
    public byte[]? ImageData { get; set; }
    public float ImageScalePercent { get; set; } = 20f; // % of background width

    // Margin from border
    public int MarginPx { get; set; } = 24;
}
