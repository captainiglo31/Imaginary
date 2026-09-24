using System.Text.Json;

namespace Imaginary.Web.Services;

public class GlobalStatsDto
{
    public long ConvertedCount { get; set; }
    public long EditedCount { get; set; }
    public long TotalActions => ConvertedCount + EditedCount;
    public long SavedBytes { get; set; }
    public double SavedMegaBytes => Math.Round(SavedBytes / (1024.0 * 1024.0), 2);
    public DateTime LastUpdatedUtc { get; set; }
}

public class GlobalStatsService : IDisposable
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<GlobalStatsService> _logger;
    private readonly string _statsFilePath;
    private readonly Timer _saveTimer;
    private bool _isDirty;
    private readonly object _saveLock = new();

    private long _convertedCount;
    private long _editedCount;
    private long _savedBytes;
    private DateTime _lastUpdatedUtc = DateTime.UtcNow;

    public event Action<GlobalStatsDto>? OnStatsChanged;

    public GlobalStatsService(IWebHostEnvironment env, ILogger<GlobalStatsService> logger)
    {
        _env = env;
        _logger = logger;

        var dataDir = Path.Combine(_env.ContentRootPath, "data");
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }
        _statsFilePath = Path.Combine(dataDir, "stats.json");

        LoadFromFile();

        // Save periodically every 5 seconds if dirty
        _saveTimer = new Timer(_ => SaveIfDirty(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public GlobalStatsDto GetStats()
    {
        return new GlobalStatsDto
        {
            ConvertedCount = Interlocked.Read(ref _convertedCount),
            EditedCount = Interlocked.Read(ref _editedCount),
            SavedBytes = Interlocked.Read(ref _savedBytes),
            LastUpdatedUtc = _lastUpdatedUtc
        };
    }

    public GlobalStatsDto Increment(long converted = 0, long edited = 0, long savedBytes = 0)
    {
        if (converted > 0) Interlocked.Add(ref _convertedCount, converted);
        if (edited > 0) Interlocked.Add(ref _editedCount, edited);
        if (savedBytes > 0) Interlocked.Add(ref _savedBytes, savedBytes);

        _lastUpdatedUtc = DateTime.UtcNow;
        _isDirty = true;

        var dto = GetStats();
        try
        {
            OnStatsChanged?.Invoke(dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while notifying stats changed subscribers");
        }

        return dto;
    }

    private void LoadFromFile()
    {
        try
        {
            if (File.Exists(_statsFilePath))
            {
                var json = File.ReadAllText(_statsFilePath);
                var dto = JsonSerializer.Deserialize<GlobalStatsDto>(json);
                if (dto != null)
                {
                    _convertedCount = dto.ConvertedCount;
                    _editedCount = dto.EditedCount;
                    _savedBytes = dto.SavedBytes;
                    _lastUpdatedUtc = dto.LastUpdatedUtc;
                    _logger.LogInformation("Loaded global stats: Converted={Converted}, Edited={Edited}, SavedBytes={SavedBytes}",
                        _convertedCount, _editedCount, _savedBytes);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load global stats from {Path}. Using default baseline.", _statsFilePath);
        }

        // Default baseline for a lively initial experience
        _convertedCount = 15840;
        _editedCount = 2390;
        _savedBytes = 89_450_000; // ~85.3 MB
        _lastUpdatedUtc = DateTime.UtcNow;
        SaveToFile();
    }

    public void SaveIfDirty()
    {
        if (!_isDirty) return;
        lock (_saveLock)
        {
            if (!_isDirty) return;
            SaveToFile();
            _isDirty = false;
        }
    }

    private void SaveToFile()
    {
        try
        {
            var dto = GetStats();
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = _statsFilePath + ".tmp";

            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _statsFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist global stats to {Path}", _statsFilePath);
        }
    }

    public void Dispose()
    {
        _saveTimer?.Dispose();
        SaveIfDirty();
    }
}
