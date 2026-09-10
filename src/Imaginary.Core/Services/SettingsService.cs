using System.Text.Json;
using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private AppSettings _settings = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Settings => _settings;

    public SettingsService(string? customPath = null)
    {
        _settingsFilePath = customPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        Reload();
    }

    public void Reload()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    _settings = loaded;
                    return;
                }
            }
        }
        catch
        {
            // Fallback to defaults
        }

        _settings = new AppSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Ignore persistence errors
        }
    }
}
