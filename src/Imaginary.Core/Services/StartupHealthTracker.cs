using System;
using System.IO;
using System.Text.Json;

namespace Imaginary.Core.Services;

public class StartupHealthState
{
    public bool IsStarting { get; set; }
    public int CrashCount { get; set; }
    public DateTime? LastStartTimeUtc { get; set; }
    public DateTime? LastCrashTimeUtc { get; set; }
}

public class StartupHealthTracker
{
    private readonly string _filePath;
    private StartupHealthState _state = new();

    public int CrashCount => _state.CrashCount;
    public bool IsCrashLoopDetected => _state.CrashCount >= 2;

    public StartupHealthTracker(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
        }
        else
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Imaginary");
            _filePath = Path.Combine(appData, "startup_health.json");
        }

        LoadState();
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                _state = JsonSerializer.Deserialize<StartupHealthState>(json) ?? new StartupHealthState();
            }
        }
        catch
        {
            _state = new StartupHealthState();
        }
    }

    private void SaveState()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Ignorieren bei Dateizugriffsfehlern
        }
    }

    /// <summary>
    /// Wird beim Start der Anwendung aufgerufen.
    /// Falls der vorherige Start nicht erfolgreich abgeschlossen wurde (IsStarting war true),
    /// wird der CrashCount erhöht.
    /// </summary>
    public void RecordStartup()
    {
        if (_state.IsStarting)
        {
            // Der letzte Start wurde nie als gesunder Zustand beendet
            _state.CrashCount++;
            _state.LastCrashTimeUtc = DateTime.UtcNow;
        }

        _state.IsStarting = true;
        _state.LastStartTimeUtc = DateTime.UtcNow;
        SaveState();
    }

    /// <summary>
    /// Wird aufgerufen, wenn die Anwendung stabil gestartet ist (z.B. nach 5-10 Sekunden im MainWindow).
    /// </summary>
    public void RecordHealthy()
    {
        _state.IsStarting = false;
        _state.CrashCount = 0;
        SaveState();
    }

    /// <summary>
    /// Setzt den Crash-Zähler manuell zurück (z.B. nach einem Rollback oder Hotfix).
    /// </summary>
    public void Reset()
    {
        _state.IsStarting = false;
        _state.CrashCount = 0;
        SaveState();
    }
}
