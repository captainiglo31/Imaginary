using System.Text.Json;
using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public class PresetManager : IPresetManager
{
    private readonly string _presetsFilePath;
    private readonly List<Preset> _userPresets = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PresetManager(string? customPresetsPath = null)
    {
        _presetsFilePath = customPresetsPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets.json");
        LoadUserPresets();
    }

    public IReadOnlyList<Preset> GetAllPresets()
    {
        var all = new List<Preset>(GetBuiltInPresets());
        all.AddRange(_userPresets);
        return all;
    }

    public Preset? GetPreset(string id)
    {
        return GetAllPresets().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void SaveUserPreset(Preset preset)
    {
        preset.IsBuiltIn = false;
        var existingIndex = _userPresets.FindIndex(p => string.Equals(p.Id, preset.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            _userPresets[existingIndex] = preset;
        }
        else
        {
            _userPresets.Add(preset);
        }

        PersistUserPresets();
    }

    public bool DeleteUserPreset(string id)
    {
        var count = _userPresets.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (count > 0)
        {
            PersistUserPresets();
            return true;
        }
        return false;
    }

    private void LoadUserPresets()
    {
        try
        {
            if (File.Exists(_presetsFilePath))
            {
                var json = File.ReadAllText(_presetsFilePath);
                var loaded = JsonSerializer.Deserialize<List<Preset>>(json, JsonOptions);
                if (loaded != null)
                {
                    _userPresets.Clear();
                    _userPresets.AddRange(loaded);
                }
            }
        }
        catch
        {
            // Ignore corrupted presets file
        }
    }

    private void PersistUserPresets()
    {
        try
        {
            var json = JsonSerializer.Serialize(_userPresets, JsonOptions);
            File.WriteAllText(_presetsFilePath, json);
        }
        catch
        {
            // Ignore persistence errors in read-only environments
        }
    }

    private static List<Preset> GetBuiltInPresets() => new()
    {
        new Preset
        {
            Id = "builtin-email",
            Name = "📧 E-Mail (kompakt)",
            Description = "Max. 1 MB Dateigröße, JPEG, max. 1920px, Metadaten gestrippt",
            IsBuiltIn = true,
            Options = new ConversionOptions
            {
                TargetFormat = ImageFormat.Jpeg,
                MaxFileSizeInBytes = 1024 * 1024, // 1 MB
                ResizeMode = ResizeMode.MaxEdge,
                MaxEdgeLength = 1920,
                DefaultQuality = 85,
                FallbackStrategy = FallbackStrategy.ResizeDown,
                StripMetadata = true
            }
        },
        new Preset
        {
            Id = "builtin-webshop",
            Name = "🛍️ Web-Shop (WebP)",
            Description = "WebP, 800x800 px (Fit), 85% Qualität, Metadaten gestrippt",
            IsBuiltIn = true,
            Options = new ConversionOptions
            {
                TargetFormat = ImageFormat.Webp,
                ResizeMode = ResizeMode.Fit,
                TargetWidth = 800,
                TargetHeight = 800,
                MaintainAspectRatio = true,
                DefaultQuality = 85,
                StripMetadata = true
            }
        },
        new Preset
        {
            Id = "builtin-social",
            Name = "📱 Social Media (Quadrat 1080x1080)",
            Description = "1080x1080 px (mittig zugeschnitten), JPEG 92%, gestrippte Metadaten",
            IsBuiltIn = true,
            Options = new ConversionOptions
            {
                TargetFormat = ImageFormat.Jpeg,
                ResizeMode = ResizeMode.FillCrop,
                TargetWidth = 1080,
                TargetHeight = 1080,
                DefaultQuality = 92,
                StripMetadata = true
            }
        },
        new Preset
        {
            Id = "builtin-archive",
            Name = "🗄️ Archiv & Druck (Verlustfrei)",
            Description = "PNG, Originalmaße, Farbreduktion bei Speicherengpass, Metadaten erhalten",
            IsBuiltIn = true,
            Options = new ConversionOptions
            {
                TargetFormat = ImageFormat.Png,
                ResizeMode = ResizeMode.None,
                FallbackStrategy = FallbackStrategy.Quantize,
                StripMetadata = false
            }
        },
        new Preset
        {
            Id = "builtin-ico",
            Name = "🪟 Windows Icon (.ico)",
            Description = "Multi-Resolution ICO (16, 32, 48, 64, 128, 256 px) für Windows & Browser",
            IsBuiltIn = true,
            Options = new ConversionOptions
            {
                TargetFormat = ImageFormat.Ico,
                ResizeMode = ResizeMode.None,
                StripMetadata = true
            }
        }
    };
}
