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

    // Update settings
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    public string? SkippedVersion { get; set; }
    public string GitHubRepositoryOwner { get; set; } = "PinoWackers";
    public string GitHubRepositoryName { get; set; } = "Imaginary";
}
