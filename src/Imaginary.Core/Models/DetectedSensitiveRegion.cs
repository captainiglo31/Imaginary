using SkiaSharp;

namespace Imaginary.Core.Models;

public enum SensitiveRegionType
{
    Face,
    LicensePlate
}

public enum RedactionStyle
{
    Pixelate,
    Blur,
    Blackout
}

public class DetectedSensitiveRegion
{
    public SKRectI Bounds { get; set; }
    public SensitiveRegionType Type { get; set; }
    public float Confidence { get; set; }
    public string Label => Type == SensitiveRegionType.Face ? "👤 Gesicht" : "🚗 Kennzeichen";

    public DetectedSensitiveRegion() { }

    public DetectedSensitiveRegion(SKRectI bounds, SensitiveRegionType type, float confidence = 0.9f)
    {
        Bounds = bounds;
        Type = type;
        Confidence = confidence;
    }
}
