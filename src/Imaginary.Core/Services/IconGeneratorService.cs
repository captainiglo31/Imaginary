using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class IconGeneratorService : IIconGeneratorService
{
    private readonly IIcoEncoder _icoEncoder;

    public IconGeneratorService(IIcoEncoder? icoEncoder = null)
    {
        _icoEncoder = icoEncoder ?? new IcoEncoder();
    }

    private static readonly List<IconTemplateItem> AllTemplates = new()
    {
        // 1. Windows (Desktop & Store)
        new IconTemplateItem
        {
            Id = "win_ico",
            Platform = IconTargetPlatform.Windows,
            FileName = "icon.ico",
            RelativeFolder = "windows",
            Width = 256,
            Height = 256,
            Format = "ico",
            Description = "Windows Multi-Resolution Icon (16, 24, 32, 48, 64, 128, 256 px)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "win_store_tile",
            Platform = IconTargetPlatform.Windows,
            FileName = "Square150x150Logo.png",
            RelativeFolder = "windows/store",
            Width = 150,
            Height = 150,
            Description = "Windows Startmenü / Store Kachel",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "win_store_small",
            Platform = IconTargetPlatform.Windows,
            FileName = "Square44x44Logo.png",
            RelativeFolder = "windows/store",
            Width = 44,
            Height = 44,
            Description = "Windows Taskleiste / App Liste Logo",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "win_store_large",
            Platform = IconTargetPlatform.Windows,
            FileName = "Square310x310Logo.png",
            RelativeFolder = "windows/store",
            Width = 310,
            Height = 310,
            Description = "Windows Große Kachel",
            DefaultSelected = false
        },

        // 2. Web & Favicons
        new IconTemplateItem
        {
            Id = "web_favicon_ico",
            Platform = IconTargetPlatform.WebFavicon,
            FileName = "favicon.ico",
            RelativeFolder = "web",
            Width = 48,
            Height = 48,
            Format = "ico",
            Description = "Multi-Size Browser Favicon (16, 32, 48 px)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "web_favicon_16",
            Platform = IconTargetPlatform.WebFavicon,
            FileName = "favicon-16x16.png",
            RelativeFolder = "web",
            Width = 16,
            Height = 16,
            Description = "Standard Browser Tab Icon (Klein)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "web_favicon_32",
            Platform = IconTargetPlatform.WebFavicon,
            FileName = "favicon-32x32.png",
            RelativeFolder = "web",
            Width = 32,
            Height = 32,
            Description = "Retina Browser Tab Icon",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "web_apple_touch",
            Platform = IconTargetPlatform.WebFavicon,
            FileName = "apple-touch-icon.png",
            RelativeFolder = "web",
            Width = 180,
            Height = 180,
            Description = "iOS Safari Lesezeichen & Home-Screen Icon",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "web_android_192",
            Platform = IconTargetPlatform.WebFavicon,
            FileName = "android-chrome-192x192.png",
            RelativeFolder = "web",
            Width = 192,
            Height = 192,
            Description = "PWA & Android Chrome Home-Screen Icon",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "web_android_512",
            Platform = IconTargetPlatform.WebFavicon,
            FileName = "android-chrome-512x512.png",
            RelativeFolder = "web",
            Width = 512,
            Height = 512,
            Description = "PWA Splash Screen Icon (HD)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "web_manifest",
            Platform = IconTargetPlatform.WebFavicon,
            FileName = "site.webmanifest",
            RelativeFolder = "web",
            Width = 0,
            Height = 0,
            Format = "json",
            Description = "PWA Web App Manifest Datei",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "web_html",
            Platform = IconTargetPlatform.WebFavicon,
            FileName = "favicon-snippet.html",
            RelativeFolder = "web",
            Width = 0,
            Height = 0,
            Format = "html",
            Description = "HTML-Code Schnipsel zum Einfügen in den <head>",
            DefaultSelected = true
        },

        // 3. Apple (iOS & macOS)
        new IconTemplateItem
        {
            Id = "ios_app_icon",
            Platform = IconTargetPlatform.AppleIosMac,
            FileName = "AppIcon-1024.png",
            RelativeFolder = "apple/ios",
            Width = 1024,
            Height = 1024,
            Description = "Apple App Store Master Icon (1024×1024 px)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "ios_iphone_60_3x",
            Platform = IconTargetPlatform.AppleIosMac,
            FileName = "AppIcon-60@3x.png",
            RelativeFolder = "apple/ios",
            Width = 180,
            Height = 180,
            Description = "iPhone App Icon (@3x Retina)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "ios_iphone_60_2x",
            Platform = IconTargetPlatform.AppleIosMac,
            FileName = "AppIcon-60@2x.png",
            RelativeFolder = "apple/ios",
            Width = 120,
            Height = 120,
            Description = "iPhone App Icon (@2x)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "ios_ipad_76_2x",
            Platform = IconTargetPlatform.AppleIosMac,
            FileName = "AppIcon-76@2x.png",
            RelativeFolder = "apple/ios",
            Width = 152,
            Height = 152,
            Description = "iPad App Icon (@2x)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "ios_spotlight",
            Platform = IconTargetPlatform.AppleIosMac,
            FileName = "AppIcon-40@2x.png",
            RelativeFolder = "apple/ios",
            Width = 80,
            Height = 80,
            Description = "iOS Spotlight Suche & Einstellungen",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "mac_icon_512",
            Platform = IconTargetPlatform.AppleIosMac,
            FileName = "icon_512x512.png",
            RelativeFolder = "apple/macos",
            Width = 512,
            Height = 512,
            Description = "macOS Dock & Finder Icon",
            DefaultSelected = true
        },

        // 4. Android
        new IconTemplateItem
        {
            Id = "android_play_store",
            Platform = IconTargetPlatform.Android,
            FileName = "playstore-icon.png",
            RelativeFolder = "android",
            Width = 512,
            Height = 512,
            Description = "Google Play Store 32-Bit PNG Icon",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "android_xxxhdpi",
            Platform = IconTargetPlatform.Android,
            FileName = "ic_launcher.png",
            RelativeFolder = "android/res/mipmap-xxxhdpi",
            Width = 192,
            Height = 192,
            Description = "Android App Icon (xxxhdpi)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "android_xxhdpi",
            Platform = IconTargetPlatform.Android,
            FileName = "ic_launcher.png",
            RelativeFolder = "android/res/mipmap-xxhdpi",
            Width = 144,
            Height = 144,
            Description = "Android App Icon (xxhdpi)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "android_xhdpi",
            Platform = IconTargetPlatform.Android,
            FileName = "ic_launcher.png",
            RelativeFolder = "android/res/mipmap-xhdpi",
            Width = 96,
            Height = 96,
            Description = "Android App Icon (xhdpi)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "android_hdpi",
            Platform = IconTargetPlatform.Android,
            FileName = "ic_launcher.png",
            RelativeFolder = "android/res/mipmap-hdpi",
            Width = 72,
            Height = 72,
            Description = "Android App Icon (hdpi)",
            DefaultSelected = true
        },
        new IconTemplateItem
        {
            Id = "android_mdpi",
            Platform = IconTargetPlatform.Android,
            FileName = "ic_launcher.png",
            RelativeFolder = "android/res/mipmap-mdpi",
            Width = 48,
            Height = 48,
            Description = "Android App Icon (mdpi)",
            DefaultSelected = true
        }
    };

    public IReadOnlyList<IconTemplateItem> GetAvailableTemplates() => AllTemplates;

    public IReadOnlyList<IconTemplateItem> GetTemplatesForPlatform(IconTargetPlatform platform) =>
        AllTemplates.Where(t => t.Platform == platform).ToList();

    public IconGenerationResult GenerateIconPackage(SKBitmap sourceBitmap, IconGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(sourceBitmap);
        ArgumentNullException.ThrowIfNull(options);

        var result = new IconGenerationResult();

        // If no items explicitly selected, default to all DefaultSelected
        var selectedIds = options.SelectedItemIds.Count > 0
            ? options.SelectedItemIds
            : new HashSet<string>(AllTemplates.Where(t => t.DefaultSelected).Select(t => t.Id), StringComparer.OrdinalIgnoreCase);

        var templatesToProcess = AllTemplates.Where(t => selectedIds.Contains(t.Id)).ToList();

        foreach (var tmpl in templatesToProcess)
        {
            if (tmpl.Format == "ico")
            {
                var sizes = tmpl.Id == "web_favicon_ico"
                    ? new[] { 16, 32, 48 }
                    : new[] { 16, 24, 32, 48, 64, 128, 256 };

                using var renderedSquare = RenderSingleIcon(
                    sourceBitmap,
                    256,
                    256,
                    options.Shape,
                    options.PaddingPercent,
                    options.BackgroundColor);

                var icoBytes = _icoEncoder.EncodeToIco(renderedSquare, sizes);
                result.Files.Add(new GeneratedIconFile
                {
                    RelativePath = tmpl.RelativePath,
                    Data = icoBytes,
                    Width = tmpl.Width,
                    Height = tmpl.Height,
                    MimeType = "image/x-icon"
                });
            }
            else if (tmpl.Format == "json")
            {
                var manifestJson = GenerateWebManifest(options.BaseName);
                result.Files.Add(new GeneratedIconFile
                {
                    RelativePath = tmpl.RelativePath,
                    Data = Encoding.UTF8.GetBytes(manifestJson),
                    MimeType = "application/manifest+json"
                });
            }
            else if (tmpl.Format == "html")
            {
                var htmlSnippet = GenerateHtmlSnippet(options.BaseName);
                result.Files.Add(new GeneratedIconFile
                {
                    RelativePath = tmpl.RelativePath,
                    Data = Encoding.UTF8.GetBytes(htmlSnippet),
                    MimeType = "text/html"
                });
            }
            else // Standard PNG icon
            {
                using var rendered = RenderSingleIcon(
                    sourceBitmap,
                    tmpl.Width,
                    tmpl.Height,
                    options.Shape,
                    options.PaddingPercent,
                    options.BackgroundColor);

                using var image = SKImage.FromBitmap(rendered);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);

                result.Files.Add(new GeneratedIconFile
                {
                    RelativePath = tmpl.RelativePath,
                    Data = data.ToArray(),
                    Width = tmpl.Width,
                    Height = tmpl.Height,
                    MimeType = "image/png"
                });
            }
        }

        return result;
    }

    public SKBitmap RenderSingleIcon(
        SKBitmap sourceBitmap,
        int width,
        int height,
        IconShape shape = IconShape.Square,
        float paddingPercent = 0f,
        SKColor? backgroundColor = null)
    {
        ArgumentNullException.ThrowIfNull(sourceBitmap);
        if (width <= 0) width = 64;
        if (height <= 0) height = 64;

        var result = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);

        var bounds = new SKRect(0, 0, width, height);

        // 1. Clip by Shape if not square
        if (shape == IconShape.Circle)
        {
            canvas.ClipRoundRect(new SKRoundRect(bounds, width / 2f, height / 2f), antialias: true);
        }
        else if (shape == IconShape.Rounded)
        {
            float radius = Math.Min(width, height) * 0.22f;
            canvas.ClipRoundRect(new SKRoundRect(bounds, radius, radius), antialias: true);
        }
        else if (shape == IconShape.Squircle)
        {
            float radius = Math.Min(width, height) * 0.28f;
            canvas.ClipRoundRect(new SKRoundRect(bounds, radius, radius), antialias: true);
        }

        // 2. Background Fill
        var bg = backgroundColor ?? SKColors.Transparent;
        if (bg.Alpha > 0)
        {
            using var bgPaint = new SKPaint { Color = bg, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(bounds, bgPaint);
        }

        // 3. Render Source Bitmap proportionally with padding
        float padX = width * Math.Clamp(paddingPercent, 0f, 0.40f);
        float padY = height * Math.Clamp(paddingPercent, 0f, 0.40f);
        float usableW = width - (padX * 2);
        float usableH = height - (padY * 2);

        float scale = Math.Min(usableW / sourceBitmap.Width, usableH / sourceBitmap.Height);
        float destW = sourceBitmap.Width * scale;
        float destH = sourceBitmap.Height * scale;
        float destX = padX + ((usableW - destW) / 2f);
        float destY = padY + ((usableH - destH) / 2f);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        canvas.DrawBitmap(sourceBitmap, new SKRect(destX, destY, destX + destW, destY + destH), paint);
        canvas.Flush();

        return result;
    }

    private static string GenerateWebManifest(string appName)
    {
        var manifest = new
        {
            name = appName,
            short_name = appName,
            icons = new[]
            {
                new { src = "/android-chrome-192x192.png", sizes = "192x192", type = "image/png" },
                new { src = "/android-chrome-512x512.png", sizes = "512x512", type = "image/png" }
            },
            theme_color = "#ffffff",
            background_color = "#ffffff",
            display = "standalone"
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string GenerateHtmlSnippet(string appName)
    {
        return $@"<!-- Imaginary Favicon & App Icon Snippet für '{appName}' -->
<link rel=""apple-touch-icon"" sizes=""180x180"" href=""/apple-touch-icon.png"">
<link rel=""icon"" type=""image/png"" sizes=""32x32"" href=""/favicon-32x32.png"">
<link rel=""icon"" type=""image/png"" sizes=""16x16"" href=""/favicon-16x16.png"">
<link rel=""icon"" type=""image/x-icon"" href=""/favicon.ico"">
<link rel=""manifest"" href=""/site.webmanifest"">
<meta name=""theme-color"" content=""#ffffff"">";
    }
}
