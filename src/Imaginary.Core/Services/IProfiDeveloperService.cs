using System.Collections.Generic;
using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public interface IProfiDeveloperService
{
    /// <summary>
    /// Entwickelt/rendert ein Bitmap mit den angegebenen RAW-/Profi-Parametern.
    /// </summary>
    SKBitmap ApplyAdjustments(SKBitmap source, ProfiAdjustmentSettings settings);

    /// <summary>
    /// Berechnet das 256-Kanal RGB- & Luminanz-Histogramm für ein Bitmap.
    /// </summary>
    ImageHistogramData ComputeHistogram(SKBitmap bitmap);

    /// <summary>
    /// Lädt ein RAW- oder Standardbild (unterstützt DNG, CR2, CR3, NEF, ARW, ORF, RW2, PEF, RAF, TIFF, JPEG, PNG, etc.).
    /// </summary>
    SKBitmap LoadRawOrStandardBitmap(string filePath);

    /// <summary>
    /// Gibt vordefinierte Profi-Entwickler-Looks (Presets) zurück.
    /// </summary>
    IReadOnlyList<ProfiPreset> GetBuiltInPresets();
}
