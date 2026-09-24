using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using SkiaSharp;

namespace Imaginary.Core.Models;

public enum IconTargetPlatform
{
    Windows,
    WebFavicon,
    AppleIosMac,
    Android
}

public enum IconShape
{
    Square,
    Rounded,
    Squircle,
    Circle
}

public class IconTemplateItem
{
    public string Id { get; init; } = string.Empty;
    public IconTargetPlatform Platform { get; init; }
    public string CategoryName => Platform switch
    {
        IconTargetPlatform.Windows => "Windows (Desktop & Store)",
        IconTargetPlatform.WebFavicon => "Web & Favicons",
        IconTargetPlatform.AppleIosMac => "Apple (iOS & macOS)",
        IconTargetPlatform.Android => "Android (Apps & Play Store)",
        _ => "Allgemein"
    };

    public string FileName { get; init; } = string.Empty;
    public string RelativeFolder { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public string Format { get; init; } = "png";
    public string Description { get; init; } = string.Empty;
    public bool DefaultSelected { get; init; } = true;

    public string RelativePath => string.IsNullOrEmpty(RelativeFolder)
        ? FileName
        : Path.Combine(RelativeFolder, FileName).Replace('\\', '/');

    public string DisplayText => $"{FileName} ({Width}×{Height} px) – {Description}";
}

public class IconGenerationOptions
{
    public HashSet<string> SelectedItemIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public IconShape Shape { get; set; } = IconShape.Square;
    public float PaddingPercent { get; set; } = 0.05f; // 5% default margin
    public SKColor BackgroundColor { get; set; } = SKColors.Transparent;
    public string BaseName { get; set; } = "app";
}

public class GeneratedIconFile
{
    public string RelativePath { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public string MimeType { get; set; } = "image/png";
}

public class IconGenerationResult
{
    public List<GeneratedIconFile> Files { get; } = new();

    public byte[] CreateZipArchive()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Files)
            {
                var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(file.Data, 0, file.Data.Length);
            }
        }
        return ms.ToArray();
    }

    public void SaveAsZip(string targetZipPath)
    {
        var dir = Path.GetDirectoryName(targetZipPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(targetZipPath, CreateZipArchive());
    }

    public void SaveToDirectory(string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Files)
        {
            var fullPath = Path.Combine(targetDirectory, file.RelativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllBytes(fullPath, file.Data);
        }
    }
}
