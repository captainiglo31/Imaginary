using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Imaginary.Core.Services;

namespace Imaginary.Core.Mcp;

public static class ImaginaryMcpFactory
{
    public static McpServer CreateServer(
        IImageEditorService? editorService = null,
        IBackgroundRemovalService? bgService = null,
        IImageFormatDetector? formatDetector = null,
        IPresetManager? presetManager = null)
    {
        editorService ??= new ImageEditorService();
        bgService ??= new BackgroundRemovalService(editorService);
        formatDetector ??= new ImageFormatDetector();

        var registry = new McpRegistry();

        // 1. Werkzeuge registrieren
        var tools = new ImaginaryMcpTools(editorService, bgService, formatDetector, presetManager: presetManager);
        registry.RegisterTools(tools);

        // 2. Resources registrieren
        registry.RegisterResource(
            uri: "imaginary://system/capabilities",
            name: "System-Fähigkeiten",
            description: "Informationen über verfügbare Codecs, Bildformate und Hardwarebeschleunigung.",
            mimeType: "application/json",
            reader: _ => Task.FromResult(JsonSerializer.Serialize(new
            {
                name = "imaginary-mcp",
                version = "2.2.3",
                supportedFormats = new[] { "JPEG", "PNG", "WebP", "AVIF", "GIF", "BMP", "TIFF", "ICO" },
                unterstuetzteFormate = new[] { "JPEG", "PNG", "WebP", "AVIF", "GIF", "BMP", "TIFF", "ICO" },
                kiModell = "U-2-Net (ONNX Runtime, 100% Offline)",
                dsgvoWerkzeuge = new[] { "Weichzeichnen (Blur)", "Verpixeln (Pixelate)", "Schwärzen (Blackout)" },
                batchVerarbeitung = true
            }, new JsonSerializerOptions { WriteIndented = true })));

        registry.RegisterResource(
            uri: "imaginary://models/u2net",
            name: "KI-Modell Status",
            description: "Aktueller Zustand des lokalen Deep-Learning Modells.",
            mimeType: "application/json",
            reader: _ => Task.FromResult(JsonSerializer.Serialize(new
            {
                modellName = "u2netp.onnx",
                installiert = bgService.IsAiModelDownloaded(),
                dateigroesseBytes = bgService.GetModelSizeBytes()
            }, new JsonSerializerOptions { WriteIndented = true })));

        // 3. Prompts / Vordefinierte Workflows registrieren
        registry.RegisterPrompt(
            name: "ecommerce_product_cleanup",
            description: "Freistellen eines Produktbildes mit KI, Konvertieren in WebP und Optimieren für Webshops.",
            args: new List<McpPromptArgument>
            {
                new() { Name = "bildpfad", Description = "Pfad zum Produktfoto", Required = true }
            },
            generator: args =>
            {
                string path = GetArg(args, "bildpfad", "imagepath", "image_path", "sourcepath", "path");
                var msgs = new List<McpPromptMessage>
                {
                    new()
                    {
                        Role = "user",
                        Content = McpContent.AsText(
                            $"Bitte bearbeite das Produktfoto '{path}':\n" +
                            $"1. Entferne den Hintergrund mit dem Tool 'remove_background' (Modus: ai).\n" +
                            $"2. Konvertiere das freigestellte Bild in 'webp' mit maximaler Qualität.\n" +
                            $"3. Melde mir die vorherige und nachherige Dateigröße.")
                    }
                };
                return Task.FromResult(msgs);
            });

        registry.RegisterPrompt(
            name: "dsgvo_redact_document",
            description: "Anonymisiert sensible Dokumente oder Gesichter in einem Bild via Verpixelung oder Schwärzung.",
            args: new List<McpPromptArgument>
            {
                new() { Name = "bildpfad", Description = "Pfad zum Bild", Required = true }
            },
            generator: args =>
            {
                string path = GetArg(args, "bildpfad", "imagepath", "image_path", "sourcepath", "path");
                var msgs = new List<McpPromptMessage>
                {
                    new()
                    {
                        Role = "user",
                        Content = McpContent.AsText(
                            $"Bitte prüfe das Dokument oder Foto '{path}' auf sensible Daten:\n" +
                            $"1. Inspiziere das Bild mit 'inspect_image'.\n" +
                            $"2. Falls personenbezogene Daten (Namen, Adressen, Kennzeichen, Gesichter) vorhanden sind, nutze 'redact_region' mit Methode 'pixelate' oder 'blackout'.\n" +
                            $"3. Speichere das Ergebnis als zensierte Version.")
                    }
                };
                return Task.FromResult(msgs);
            });

        return new McpServer(registry);
    }

    private static string GetArg(Dictionary<string, string> args, params string[] keys)
    {
        foreach (var k in keys)
        {
            foreach (var kv in args)
            {
                if (string.Equals(kv.Key, k, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
        }
        return "";
    }
}
