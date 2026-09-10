namespace Imaginary.Core.Models;

public class ConversionOptions
{
    /// <summary>
    /// Target image format, or null to keep the original detected format.
    /// </summary>
    public ImageFormat? TargetFormat { get; set; }

    /// <summary>
    /// Maximum allowed file size in bytes. Null if unconstrained.
    /// </summary>
    public long? MaxFileSizeInBytes { get; set; }

    /// <summary>
    /// Resize mode: None, Percentage, AbsolutePixels, Fit, FillCrop, Pad, MaxEdge.
    /// </summary>
    public ResizeMode ResizeMode { get; set; } = ResizeMode.None;

    /// <summary>
    /// Scaling percentage for ResizeMode.Percentage (e.g. 50.0 for 50%).
    /// </summary>
    public double ResizePercentage { get; set; } = 100.0;

    /// <summary>
    /// Target width in pixels for ResizeMode.AbsolutePixels, Fit, FillCrop, Pad.
    /// </summary>
    public int? TargetWidth { get; set; }

    /// <summary>
    /// Target height in pixels for ResizeMode.AbsolutePixels, Fit, FillCrop, Pad.
    /// </summary>
    public int? TargetHeight { get; set; }

    /// <summary>
    /// Longest edge length in pixels for ResizeMode.MaxEdge.
    /// </summary>
    public int? MaxEdgeLength { get; set; }

    /// <summary>
    /// Background color hex code (e.g. "#FFFFFF" or "transparent") when using ResizeMode.Pad.
    /// </summary>
    public string PadColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// Whether to preserve the aspect ratio when resizing.
    /// </summary>
    public bool MaintainAspectRatio { get; set; } = true;

    /// <summary>
    /// Strategy to use when lossless formats exceed MaxFileSizeInBytes or lossy formats cannot meet the constraint.
    /// </summary>
    public FallbackStrategy FallbackStrategy { get; set; } = FallbackStrategy.ResizeDown;

    /// <summary>
    /// Output directory where converted files will be saved (when saving to disk).
    /// </summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// Default encoding quality (1-100) for JPEG and WebP.
    /// </summary>
    public int DefaultQuality { get; set; } = 90;

    /// <summary>
    /// Strip EXIF, GPS and camera metadata for privacy and size reduction (default true).
    /// </summary>
    public bool StripMetadata { get; set; } = true;

    /// <summary>
    /// Optional watermark options.
    /// </summary>
    public WatermarkOptions Watermark { get; set; } = new();
}
