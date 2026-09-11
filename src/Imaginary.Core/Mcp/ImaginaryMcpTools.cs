using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using SkiaSharp;

namespace Imaginary.Core.Mcp;

public class ImaginaryMcpTools
{
    private readonly IImageEditorService _editorService;
    private readonly IBackgroundRemovalService _bgService;
    private readonly IImageFormatDetector _formatDetector;
    private readonly IWatermarkService _watermarkService;
    private readonly IIcoEncoder _icoEncoder;
    private readonly IPresetManager? _presetManager;

    public ImaginaryMcpTools(
        IImageEditorService editorService,
        IBackgroundRemovalService bgService,
        IImageFormatDetector formatDetector,
        IWatermarkService? watermarkService = null,
        IIcoEncoder? icoEncoder = null,
        IPresetManager? presetManager = null)
    {
        _editorService = editorService ?? throw new ArgumentNullException(nameof(editorService));
        _bgService = bgService ?? throw new ArgumentNullException(nameof(bgService));
        _formatDetector = formatDetector ?? throw new ArgumentNullException(nameof(formatDetector));
        _watermarkService = watermarkService ?? new WatermarkService();
        _icoEncoder = icoEncoder ?? new IcoEncoder();
        _presetManager = presetManager;
    }

    [McpTool("convert_image", "Konvertiert ein Bild in ein Zielformat (JPEG, PNG, WebP, AVIF, GIF, BMP, TIFF, ICO) mit Qualitäts- und Skalierungsoptionen.")]
    public McpToolResult ConvertImage(
        [McpParameter("Absoluter Pfad zur Eingabedatei", Required = true)] string inputPath,
        [McpParameter("Zielformat (jpeg, png, webp, avif, gif, bmp, tiff, ico)", Required = true)] string format,
        [McpParameter("Qualitätsstufe von 1 bis 100 (Standard: 85)")] int quality = 85,
        [McpParameter("Optionale Zielbreite in Pixeln")] int? width = null,
        [McpParameter("Optionale Zielhöhe in Pixeln")] int? height = null,
        [McpParameter("Optionaler Zielpfad. Standard: Unterordner 'converted' neben der Originaldatei.")] string? outputPath = null)
    {
        if (!File.Exists(inputPath))
            return McpToolResult.Error($"Eingabedatei nicht gefunden: {inputPath}");

        using var inStream = File.OpenRead(inputPath);
        using var original = SKBitmap.Decode(inStream);
        if (original == null)
            return McpToolResult.Error($"Konnte Bild '{Path.GetFileName(inputPath)}' nicht dekodieren.");

        SKBitmap working = original;
        bool disposedWorking = false;

        try
        {
            if (width.HasValue && height.HasValue && (width.Value != original.Width || height.Value != original.Height))
            {
                var resized = original.Resize(new SKImageInfo(width.Value, height.Value), SKFilterQuality.High);
                if (resized != null)
                {
                    working = resized;
                    disposedWorking = true;
                }
            }

            var targetFormat = ParseFormat(format);
            string outPath = ResolveOutputPath(inputPath, outputPath, targetFormat);

            SaveBitmap(working, outPath, targetFormat, quality);

            long inBytes = new FileInfo(inputPath).Length;
            long outBytes = new FileInfo(outPath).Length;
            double savings = inBytes > 0 ? (1.0 - (double)outBytes / inBytes) * 100.0 : 0;

            return McpToolResult.Text(
                $"Erfolgreich konvertiert!\n" +
                $"- Ziel: {outPath}\n" +
                $"- Format: {targetFormat}\n" +
                $"- Auflösung: {working.Width} × {working.Height} px\n" +
                $"- Vorher: {FormatBytes(inBytes)} -> Nachher: {FormatBytes(outBytes)} ({savings:F1}% Ersparnis)");
        }
        finally
        {
            if (disposedWorking) working.Dispose();
        }
    }

    [McpTool("remove_background", "Entfernt den Hintergrund eines Bildes mittels Deep-Learning-KI (U-2-Net ONNX) oder Farbschlüsselung (Magic Wand) und erzeugt ein transparentes PNG.")]
    public async Task<McpToolResult> RemoveBackgroundAsync(
        [McpParameter("Absoluter Pfad zum Bild", Required = true)] string inputPath,
        [McpParameter("Modus: 'ai' (Deep-Learning-KI) oder 'color' (Farbe entfernen)", Required = true)] string mode = "ai",
        [McpParameter("Hintergrundfarbe als Hex-Wert (#FFFFFF, #00FF00 etc.), wenn mode='color'")] string? colorHex = null,
        [McpParameter("Farbtoleranz (0.01 bis 0.60, Standard 0.15), wenn mode='color'")] float tolerance = 0.15f,
        [McpParameter("Optionaler Zielpfad")] string? outputPath = null)
    {
        if (!File.Exists(inputPath))
            return McpToolResult.Error($"Datei nicht gefunden: {inputPath}");

        using var inStream = File.OpenRead(inputPath);
        using var original = SKBitmap.Decode(inStream);
        if (original == null)
            return McpToolResult.Error($"Konnte Bild '{Path.GetFileName(inputPath)}' nicht laden.");

        SKBitmap resultBitmap;

        if (string.Equals(mode, "ai", StringComparison.OrdinalIgnoreCase))
        {
            if (!_bgService.IsAiModelDownloaded())
            {
                return McpToolResult.Error("Das KI-Modell ist noch nicht heruntergeladen. Führe zuerst das Werkzeug 'download_ai_model' aus.");
            }

            resultBitmap = await _bgService.RemoveBackgroundAsync(original, BackgroundRemovalMode.AiOnnx);
        }
        else
        {
            SKColor color = SKColors.White;
            if (!string.IsNullOrWhiteSpace(colorHex) && SKColor.TryParse(colorHex, out var parsed))
            {
                color = parsed;
            }
            resultBitmap = _editorService.RemoveBackgroundByColor(original, color, tolerance);
        }

        string outPath = ResolveOutputPath(inputPath, outputPath, ImageFormat.Png, suffix: "_transparent");
        SaveBitmap(resultBitmap, outPath, ImageFormat.Png, 100);
        resultBitmap.Dispose();

        return McpToolResult.Text(
            $"Hintergrund erfolgreich entfernt!\n" +
            $"- Gespeichert unter: {outPath}\n" +
            $"- Modus: {mode.ToUpperInvariant()}\n" +
            $"- Format: PNG (Transparenz erhalten)");
    }

    [McpTool("segment_object_by_region", "Schneidet ein bestimmtes Objekt innerhalb einer Bounding-Box [x, y, width, height] gezielt mit KI frei. Der Rest wird transparent.")]
    public async Task<McpToolResult> SegmentObjectByRegionAsync(
        [McpParameter("Absoluter Pfad zum Bild", Required = true)] string inputPath,
        [McpParameter("Linke X-Koordinate der Bounding-Box", Required = true)] int x,
        [McpParameter("Obere Y-Koordinate der Bounding-Box", Required = true)] int y,
        [McpParameter("Breite des Bereichs", Required = true)] int width,
        [McpParameter("Höhe des Bereichs", Required = true)] int height,
        [McpParameter("Optionaler Zielpfad")] string? outputPath = null)
    {
        if (!File.Exists(inputPath))
            return McpToolResult.Error($"Datei nicht gefunden: {inputPath}");

        if (!_bgService.IsAiModelDownloaded())
            return McpToolResult.Error("Das KI-Modell ist noch nicht heruntergeladen. Führe zuerst das Werkzeug 'download_ai_model' aus.");

        using var inStream = File.OpenRead(inputPath);
        using var original = SKBitmap.Decode(inStream);
        if (original == null)
            return McpToolResult.Error("Bild konnte nicht geladen werden.");

        var roi = new SKRectI(x, y, x + width, y + height);
        var cutout = await _bgService.SegmentObjectRegionAsync(original, roi);

        string outPath = ResolveOutputPath(inputPath, outputPath, ImageFormat.Png, suffix: "_cutout");
        SaveBitmap(cutout, outPath, ImageFormat.Png, 100);
        cutout.Dispose();

        return McpToolResult.Text(
            $"Objekt erfolgreich freigestellt!\n" +
            $"- Ziel: {outPath}\n" +
            $"- Erkannte Bounding-Box: [{x}, {y}, {width} × {height} px]\n" +
            $"- Format: PNG mit transparentem Alpha");
    }

    [McpTool("redact_region", "DSGVO-konforme Zensur: Verwischen (blur), Verpixeln (pixelate) oder Schwärzen (blackout) eines rechteckigen Bildbereichs.")]
    public McpToolResult RedactRegion(
        [McpParameter("Absoluter Pfad zum Bild", Required = true)] string inputPath,
        [McpParameter("Linke X-Koordinate", Required = true)] int x,
        [McpParameter("Obere Y-Koordinate", Required = true)] int y,
        [McpParameter("Breite der Region", Required = true)] int width,
        [McpParameter("Höhe der Region", Required = true)] int height,
        [McpParameter("Zensur-Methode ('blur', 'pixelate', 'blackout')", Required = true)] string style = "pixelate",
        [McpParameter("Blockgröße für Pixelierung (z.B. 16)")] int pixelSize = 16,
        [McpParameter("Optionaler Zielpfad")] string? outputPath = null)
    {
        if (!File.Exists(inputPath)) return McpToolResult.Error($"Datei nicht gefunden: {inputPath}");

        using var inStream = File.OpenRead(inputPath);
        using var original = SKBitmap.Decode(inStream);
        if (original == null) return McpToolResult.Error("Bild konnte nicht geladen werden.");

        var rect = new SKRectI(x, y, x + width, y + height);
        SKBitmap redacted = style.ToLowerInvariant() switch
        {
            "blur" => _editorService.ApplyGaussianBlur(original, rect, 14f),
            "blackout" => _editorService.ApplyBlackout(original, rect, SKColors.Black),
            _ => _editorService.ApplyPixelate(original, rect, pixelSize)
        };

        var format = ParseFormat(Path.GetExtension(inputPath).TrimStart('.'));
        string outPath = ResolveOutputPath(inputPath, outputPath, format, suffix: "_redacted");
        SaveBitmap(redacted, outPath, format, 92);
        redacted.Dispose();

        return McpToolResult.Text(
            $"Bereich DSGVO-konform zensiert!\n" +
            $"- Ziel: {outPath}\n" +
            $"- Methode: {style.ToUpperInvariant()}\n" +
            $"- Bereich: [{x}, {y}, {width} × {height} px]");
    }

    [McpTool("crop_image", "Schneidet einen definierten Bildbereich verlustfrei zu.")]
    public McpToolResult CropImage(
        [McpParameter("Absoluter Pfad zum Bild", Required = true)] string inputPath,
        [McpParameter("Linke X-Koordinate", Required = true)] int x,
        [McpParameter("Obere Y-Koordinate", Required = true)] int y,
        [McpParameter("Breite", Required = true)] int width,
        [McpParameter("Höhe", Required = true)] int height,
        [McpParameter("Optionaler Zielpfad")] string? outputPath = null)
    {
        if (!File.Exists(inputPath)) return McpToolResult.Error($"Datei nicht gefunden: {inputPath}");

        using var inStream = File.OpenRead(inputPath);
        using var original = SKBitmap.Decode(inStream);
        if (original == null) return McpToolResult.Error("Bild konnte nicht geladen werden.");

        var rect = new SKRectI(x, y, x + width, y + height);
        using var cropped = _editorService.Crop(original, rect);

        var format = ParseFormat(Path.GetExtension(inputPath).TrimStart('.'));
        string outPath = ResolveOutputPath(inputPath, outputPath, format, suffix: "_crop");
        SaveBitmap(cropped, outPath, format, 92);

        return McpToolResult.Text(
            $"Bild erfolgreich zugeschnitten!\n" +
            $"- Ziel: {outPath}\n" +
            $"- Neue Dimensionen: {cropped.Width} × {cropped.Height} px");
    }

    [McpTool("apply_watermark", "Fügt einem Bild ein Text-Wasserzeichen mit wählbarer Deckkraft und Position hinzu.")]
    public McpToolResult ApplyWatermark(
        [McpParameter("Absoluter Pfad zum Bild", Required = true)] string inputPath,
        [McpParameter("Wasserzeichen-Text", Required = true)] string text,
        [McpParameter("Position ('bottomright', 'topleft', 'topright', 'bottomleft', 'center')")] string position = "bottomright",
        [McpParameter("Transparenz von 0.1 bis 1.0 (Standard 0.6)")] float opacity = 0.6f,
        [McpParameter("Schriftgröße (Standard: 32)")] int fontSize = 32,
        [McpParameter("Schriftfarbe als Hex (Standard: #FFFFFF)")] string colorHex = "#FFFFFF",
        [McpParameter("Optionaler Zielpfad")] string? outputPath = null)
    {
        if (!File.Exists(inputPath)) return McpToolResult.Error($"Datei nicht gefunden: {inputPath}");

        using var inStream = File.OpenRead(inputPath);
        using var original = SKBitmap.Decode(inStream);
        if (original == null) return McpToolResult.Error("Bild konnte nicht geladen werden.");

        var wmPos = position.ToLowerInvariant() switch
        {
            "topleft" => WatermarkPosition.TopLeft,
            "topright" => WatermarkPosition.TopRight,
            "bottomleft" => WatermarkPosition.BottomLeft,
            "center" => WatermarkPosition.Center,
            _ => WatermarkPosition.BottomRight
        };

        var options = new WatermarkOptions
        {
            Type = WatermarkType.Text,
            Text = text,
            Position = wmPos,
            Opacity = opacity,
            FontSize = fontSize,
            TextColor = colorHex
        };

        _watermarkService.ApplyWatermark(original, options);

        var format = ParseFormat(Path.GetExtension(inputPath).TrimStart('.'));
        string outPath = ResolveOutputPath(inputPath, outputPath, format, suffix: "_watermarked");
        SaveBitmap(original, outPath, format, 92);

        return McpToolResult.Text(
            $"Wasserzeichen '{text}' platziert!\n" +
            $"- Ziel: {outPath}\n" +
            $"- Position: {position}\n" +
            $"- Deckkraft: {opacity:P0}");
    }

    [McpTool("generate_icon_set", "Generiert aus einem Master-Bild eine Multi-Resolution Windows .ico-Datei (16, 32, 48, 64, 128, 256 px).")]
    public McpToolResult GenerateIconSet(
        [McpParameter("Absoluter Pfad zum Quellbild", Required = true)] string inputPath,
        [McpParameter("Optionaler Zielpfad für die .ico Datei")] string? outputPath = null)
    {
        if (!File.Exists(inputPath)) return McpToolResult.Error($"Datei nicht gefunden: {inputPath}");

        using var inStream = File.OpenRead(inputPath);
        using var original = SKBitmap.Decode(inStream);
        if (original == null) return McpToolResult.Error("Bild konnte nicht geladen werden.");

        string outPath = ResolveOutputPath(inputPath, outputPath, ImageFormat.Ico);
        using var outStream = File.Create(outPath);
        _icoEncoder.SaveToIco(original, outStream);

        return McpToolResult.Text(
            $"Windows Icon-Set erfolgreich generiert!\n" +
            $"- Ziel: {outPath}\n" +
            $"- Enthaltene Auflösungen: 16x16, 32x32, 48x48, 64x64, 128x128, 256x256 px");
    }

    [McpTool("inspect_image", "Analysiert ein Bild detailliert: Abmessungen, Farbtiefe, Alpha-Kanal, Dateiformat und EXIF-Metadaten.")]
    public McpToolResult InspectImage(
        [McpParameter("Absoluter Pfad zum Bild", Required = true)] string inputPath)
    {
        if (!File.Exists(inputPath)) return McpToolResult.Error($"Datei nicht gefunden: {inputPath}");

        var fileInfo = new FileInfo(inputPath);
        var detectedFormat = _formatDetector.DetectFormat(inputPath);

        using var inStream = File.OpenRead(inputPath);
        using var codec = SKCodec.Create(inStream);
        if (codec == null)
            return McpToolResult.Error("Konnte Bildmetadaten mit SkiaSharp nicht parsen.");

        var info = codec.Info;
        bool hasAlpha = info.ColorType == SKColorType.Rgba8888 || info.AlphaType != SKAlphaType.Opaque;

        var details = new
        {
            width = info.Width,
            height = info.Height,
            format = detectedFormat.ToString(),
            hasAlpha = hasAlpha,
            fileName = fileInfo.Name,
            filePath = fileInfo.FullName,
            fileSizeBytes = fileInfo.Length,
            fileSizeFormatted = FormatBytes(fileInfo.Length),
            aspectRatio = Math.Round((double)info.Width / info.Height, 2),
            colorType = info.ColorType.ToString(),
            // Deutsche Aliasse für Lokalisierung
            breitePx = info.Width,
            hoehePx = info.Height,
            formatErkannt = detectedFormat.ToString(),
            alphaKanal = hasAlpha ? "Vorhanden" : "Nicht vorhanden"
        };

        return McpToolResult.Text(JsonSerializer.Serialize(details, new JsonSerializerOptions { WriteIndented = true }));
    }

    [McpTool("check_ai_model", "Überprüft den Installations-Status des lokalen Deep-Learning KI-Modells (U-2-Net ONNX).")]
    public McpToolResult CheckAiModel()
    {
        bool downloaded = _bgService.IsAiModelDownloaded();
        if (downloaded)
        {
            long size = _bgService.GetModelSizeBytes();
            return McpToolResult.Text(
                $"🟢 KI-Modell ist vollständig einsatzbereit!\n" +
                $"- Typ: U-2-Net Portable Deep Learning\n" +
                $"- Dateigröße: {FormatBytes(size)}\n" +
                $"- Offline-fähig: Ja (100% lokal ohne Cloud)");
        }
        else
        {
            return McpToolResult.Text(
                "⚪ KI-Modell ist noch nicht installiert.\n" +
                "Rufe das Werkzeug 'download_ai_model' auf, um das Modell (~4.7 MB) herunterzuladen.");
        }
    }

    [McpTool("download_ai_model", "Lädt das lokale U-2-Net ONNX Deep-Learning Modell herunter, falls es noch nicht vorhanden ist.")]
    public async Task<McpToolResult> DownloadAiModelAsync()
    {
        if (_bgService.IsAiModelDownloaded())
        {
            return McpToolResult.Text("KI-Modell ist bereits heruntergeladen und einsatzbereit.");
        }

        var progress = new Progress<double>();
        bool ok = await _bgService.DownloadModelAsync(progress);

        if (ok)
        {
            return McpToolResult.Text("KI-Modell (U-2-Net) wurde erfolgreich heruntergeladen und ist jetzt offline einsatzbereit!");
        }
        else
        {
            return McpToolResult.Error("Download des KI-Modells fehlgeschlagen. Bitte Internetverbindung prüfen.");
        }
    }

    private static ImageFormat ParseFormat(string str) => str.ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => ImageFormat.Jpeg,
        "png" => ImageFormat.Png,
        "webp" => ImageFormat.Webp,
        "avif" => ImageFormat.Avif,
        "gif" => ImageFormat.Gif,
        "bmp" => ImageFormat.Bmp,
        "tiff" or "tif" => ImageFormat.Tiff,
        "ico" => ImageFormat.Ico,
        _ => ImageFormat.Webp
    };

    private static string ResolveOutputPath(string inputPath, string? requestedOutput, ImageFormat format, string suffix = "")
    {
        if (!string.IsNullOrWhiteSpace(requestedOutput))
        {
            var dir = Path.GetDirectoryName(requestedOutput);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return requestedOutput;
        }

        string inputDir = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        string convertedDir = Path.Combine(inputDir, "converted");
        if (!Directory.Exists(convertedDir)) Directory.CreateDirectory(convertedDir);

        string ext = format switch
        {
            ImageFormat.Jpeg => ".jpg",
            ImageFormat.Png => ".png",
            ImageFormat.Webp => ".webp",
            ImageFormat.Avif => ".avif",
            ImageFormat.Gif => ".gif",
            ImageFormat.Bmp => ".bmp",
            ImageFormat.Tiff => ".tiff",
            ImageFormat.Ico => ".ico",
            _ => ".png"
        };

        return Path.Combine(convertedDir, $"{fileNameWithoutExt}{suffix}{ext}");
    }

    private static void SaveBitmap(SKBitmap bitmap, string outputPath, ImageFormat format, int quality)
    {
        var encodedFormat = format switch
        {
            ImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            ImageFormat.Png => SKEncodedImageFormat.Png,
            ImageFormat.Webp => SKEncodedImageFormat.Webp,
            ImageFormat.Avif => SKEncodedImageFormat.Avif,
            ImageFormat.Gif => SKEncodedImageFormat.Gif,
            ImageFormat.Bmp => SKEncodedImageFormat.Bmp,
            _ => SKEncodedImageFormat.Png
        };

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(encodedFormat, Math.Clamp(quality, 1, 100));
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int i = 0;
        double d = bytes;
        while (d >= 1024 && i < suffixes.Length - 1)
        {
            d /= 1024;
            i++;
        }
        return $"{d:0.##} {suffixes[i]}";
    }
}
