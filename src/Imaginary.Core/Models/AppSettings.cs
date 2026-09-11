namespace Imaginary.Core.Models;

public class AppSettings
{
    public bool IsDarkMode { get; set; } = false;
    public bool ExplorerIntegrationEnabled { get; set; } = false;
    public string? LastOutputDirectory { get; set; }
    public string? SelectedPresetId { get; set; }

    // Hotfolder settings
    public string? HotfolderPath { get; set; }
    public string? HotfolderOutputDir { get; set; }
    public bool HotfolderAutoStart { get; set; } = false;
    public bool HotfolderIncludeSubdirectories { get; set; } = false;
    public HotfolderOriginalAction HotfolderOriginalAction { get; set; } = HotfolderOriginalAction.MoveToSubfolder;
    public string HotfolderOriginalSubfolder { get; set; } = "Originale";
    public string? HotfolderSelectedPresetId { get; set; }
    public int HotfolderTodayProcessedCount { get; set; } = 0;
    public long HotfolderTodaySavedBytes { get; set; } = 0;
    public DateTime? HotfolderStatsDate { get; set; }

    // System & Autostart settings
    public bool IsAutostartEnabled { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool StartMinimizedInTray { get; set; } = false;
    public bool ShowTrayNotifications { get; set; } = true;
    public bool HasShownTrayIntroBalloon { get; set; } = false;

    // Update settings
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    public string? SkippedVersion { get; set; }
    public string GitHubRepositoryOwner { get; set; } = "captainiglo31";
    public string GitHubRepositoryName { get; set; } = "Imaginary";

    // Background Removal & AI
    public BackgroundRemovalMode PreferredBackgroundRemovalMode { get; set; } = BackgroundRemovalMode.ColorKey;
    public float ColorKeyTolerance { get; set; } = 0.15f;
    public bool AiModelDownloaded { get; set; }
}
