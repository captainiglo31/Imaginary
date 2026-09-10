namespace Imaginary.Core.Models;

public class UpdateInfo
{
    public bool IsUpdateAvailable { get; init; }
    public Version CurrentVersion { get; init; } = new(1, 0, 0);
    public Version? LatestVersion { get; init; }
    public string? TagName { get; init; }
    public string? ReleaseTitle { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? DownloadUrl { get; init; }
    public long FileSizeBytes { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string? HtmlUrl { get; init; }
    public string? ErrorMessage { get; init; }

    public string FormattedFileSize
    {
        get
        {
            if (FileSizeBytes <= 0) return string.Empty;
            return $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
