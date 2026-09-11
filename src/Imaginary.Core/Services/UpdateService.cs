using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly Version _currentVersion;

    public Version CurrentVersion => _currentVersion;

    public UpdateService(HttpClient? httpClient = null, Version? currentVersion = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _currentVersion = currentVersion ?? ResolveCurrentVersion();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Imaginary-Desktop/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    private static Version ResolveCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        return ver != null ? new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build)) : new Version(1, 0, 0);
    }

    public async Task<UpdateInfo> CheckForUpdateAsync(string owner, string repo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return new UpdateInfo
            {
                IsUpdateAvailable = false,
                CurrentVersion = _currentVersion,
                ErrorMessage = "Kein GitHub-Repository konfiguriert."
            };
        }

        try
        {
            var url = $"https://api.github.com/repos/{owner.Trim()}/{repo.Trim()}/releases/latest";
            using var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new UpdateInfo
                    {
                        IsUpdateAvailable = false,
                        CurrentVersion = _currentVersion,
                        ErrorMessage = $"Noch keine Releases im Repository '{owner}/{repo}' gefunden."
                    };
                }

                return new UpdateInfo
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = _currentVersion,
                    ErrorMessage = $"GitHub-Anfrage fehlgeschlagen: {(int)response.StatusCode} {response.ReasonPhrase}"
                };
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseReleaseJson(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateInfo
            {
                IsUpdateAvailable = false,
                CurrentVersion = _currentVersion,
                ErrorMessage = $"Fehler bei der Update-Prüfung: {ex.Message}"
            };
        }
    }

    public UpdateInfo ParseReleaseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var releaseTitle = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : tagName;
            var releaseNotes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : string.Empty;
            var htmlUrl = root.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() : null;

            DateTimeOffset? publishedAt = null;
            if (root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTimeOffset(out var dt))
            {
                publishedAt = dt;
            }

            // Version aus Tag parsen (z.B. "v1.1.0" -> 1.1.0)
            var latestVersion = ParseVersionFromTag(tagName);
            if (latestVersion == null)
            {
                return new UpdateInfo
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = _currentVersion,
                    ErrorMessage = $"Ungültiges Versions-Tag im Release: '{tagName}'"
                };
            }

            // Passendes Download-Asset suchen (z.B. Imaginary.exe)
            string? downloadUrl = null;
            long fileSizeBytes = 0;

            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                // Bevorzuge .exe
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    var assetName = asset.TryGetProperty("name", out var an) ? an.GetString() : string.Empty;
                    if (assetName != null && assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var bdu) ? bdu.GetString() : null;
                        fileSizeBytes = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                        break;
                    }
                }

                // Fallback: Wenn keine direkte .exe, nach .zip suchen
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        var assetName = asset.TryGetProperty("name", out var an) ? an.GetString() : string.Empty;
                        if (assetName != null && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var bdu) ? bdu.GetString() : null;
                            fileSizeBytes = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                            break;
                        }
                    }
                }
            }

            var isNewer = latestVersion > _currentVersion;

            return new UpdateInfo
            {
                IsUpdateAvailable = isNewer,
                CurrentVersion = _currentVersion,
                LatestVersion = latestVersion,
                TagName = tagName,
                ReleaseTitle = releaseTitle,
                ReleaseNotes = releaseNotes,
                DownloadUrl = downloadUrl,
                FileSizeBytes = fileSizeBytes,
                PublishedAt = publishedAt,
                HtmlUrl = htmlUrl
            };
        }
        catch (Exception ex)
        {
            return new UpdateInfo
            {
                IsUpdateAvailable = false,
                CurrentVersion = _currentVersion,
                ErrorMessage = $"Fehler beim Parsen der Release-Informationen: {ex.Message}"
            };
        }
    }

    public static Version? ParseVersionFromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var cleaned = tag.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(1).Trim();
        }

        // Trenne eventuelle Pre-Release Suffixe wie -beta oder -preview ab
        var dashIndex = cleaned.IndexOf('-');
        if (dashIndex >= 0)
        {
            cleaned = cleaned.Substring(0, dashIndex);
        }

        if (Version.TryParse(cleaned, out var parsed))
        {
            var major = parsed.Major;
            var minor = parsed.Minor < 0 ? 0 : parsed.Minor;
            var build = parsed.Build < 0 ? 0 : parsed.Build;
            return new Version(major, minor, build);
        }

        // Falls unvollständig (z.B. nur Zahl "2")
        if (int.TryParse(cleaned, out var singleMajor))
        {
            return new Version(singleMajor, 0, 0);
        }

        return null;
    }

    public async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new ArgumentException("Download-URL darf nicht leer sein.", nameof(downloadUrl));

        var tempDir = Path.Combine(Path.GetTempPath(), "Imaginary_Update");
        Directory.CreateDirectory(tempDir);
        var tempFilePath = Path.Combine(tempDir, $"Imaginary_New_{Guid.NewGuid():N}.exe");

        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                var percentage = (double)totalRead / totalBytes;
                progress.Report(percentage);
            }
        }

        progress?.Report(1.0);
        return tempFilePath;
    }

    public bool ApplyUpdateAndRestart(string downloadedFilePath, string? targetExecutablePath = null, bool startProcess = true)
    {
        if (!File.Exists(downloadedFilePath))
            throw new FileNotFoundException("Die heruntergeladene Update-Datei wurde nicht gefunden.", downloadedFilePath);

        var currentExe = targetExecutablePath ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
        {
            currentExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Imaginary.exe");
        }

        var dir = Path.GetDirectoryName(currentExe) ?? AppDomain.CurrentDomain.BaseDirectory;
        var exeName = Path.GetFileNameWithoutExtension(currentExe);
        var previousExe = Path.Combine(dir, $"{exeName}.previous.exe");
        var oldExe = Path.Combine(dir, $"{exeName}.old");

        // 1. Zuvor verbliebene alte Rollback-Dateien aufräumen
        try
        {
            if (File.Exists(oldExe)) { try { File.Delete(oldExe); } catch { } }
            if (File.Exists(previousExe)) { try { File.Delete(previousExe); } catch { } }
        }
        catch { }

        // 2. Metadaten der aktuellen Version sichern
        try
        {
            var metaPath = Path.Combine(dir, $"{exeName}_backup_info.json");
            var meta = new
            {
                previousVersion = _currentVersion.ToString(),
                backupPath = previousExe,
                timestamp = DateTime.UtcNow.ToString("O")
            };
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }

        // 3. Die aktuell laufende Executable umbenennen in .old und .previous.exe.
        // Windows NTFS erlaubt das Umbenennen einer laufenden .exe-Datei im selben Ordner uneingeschränkt!
        File.Move(currentExe, oldExe, overwrite: true);
        try
        {
            File.Copy(oldExe, previousExe, overwrite: true);
        }
        catch { }

        // 4. Die neu heruntergeladene Datei an den ursprünglichen Speicherort der .exe bewegen
        try
        {
            File.Move(downloadedFilePath, currentExe, overwrite: true);
        }
        catch
        {
            File.Copy(downloadedFilePath, currentExe, overwrite: true);
            try { File.Delete(downloadedFilePath); } catch { }
        }

        if (!startProcess)
        {
            return true;
        }

        // 5. Die aktualisierte Anwendung direkt und nativ starten (keine Shell, keine PowerShell, kein CMD!)
        var startInfo = new ProcessStartInfo
        {
            FileName = currentExe,
            UseShellExecute = true,
            WorkingDirectory = dir
        };

        var proc = Process.Start(startInfo);
        return proc != null;
    }

    public bool CanRollback(out string? previousVersion, out string? backupPath, string? targetExecutablePath = null)
    {
        previousVersion = null;
        backupPath = null;

        var currentExe = targetExecutablePath ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
        {
            currentExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Imaginary.exe");
        }

        var dir = Path.GetDirectoryName(currentExe) ?? AppDomain.CurrentDomain.BaseDirectory;
        var exeName = Path.GetFileNameWithoutExtension(currentExe);
        var primaryBackup = Path.Combine(dir, $"{exeName}.previous.exe");
        var legacyBackup = Path.Combine(dir, $"{exeName}.old");

        string? foundBackup = null;
        if (File.Exists(primaryBackup)) foundBackup = primaryBackup;
        else if (File.Exists(legacyBackup)) foundBackup = legacyBackup;

        if (foundBackup == null) return false;

        backupPath = foundBackup;

        // Versuche Version aus Metadaten zu lesen
        try
        {
            var metaPath = Path.Combine(dir, $"{exeName}_backup_info.json");
            if (File.Exists(metaPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
                if (doc.RootElement.TryGetProperty("previousVersion", out var prop))
                {
                    previousVersion = prop.GetString();
                }
            }
        }
        catch { }

        // Fallback: PE File Version Info
        if (string.IsNullOrWhiteSpace(previousVersion))
        {
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(foundBackup);
                previousVersion = fvi.ProductVersion ?? fvi.FileVersion ?? "Vorherige Version";
            }
            catch
            {
                previousVersion = "Vorherige Version";
            }
        }

        return true;
    }

    public bool RollbackToPreviousVersion(bool restart = true, string? targetExecutablePath = null)
    {
        if (!CanRollback(out _, out var backupPath, targetExecutablePath) || backupPath == null)
        {
            return false;
        }

        var currentExe = targetExecutablePath ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe))
        {
            currentExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Imaginary.exe");
        }

        var dir = Path.GetDirectoryName(currentExe) ?? AppDomain.CurrentDomain.BaseDirectory;
        var exeName = Path.GetFileNameWithoutExtension(currentExe);
        var brokenBackup = Path.Combine(dir, $"{exeName}.broken_{DateTime.Now:yyyyMMdd_HHmmss}.old");

        try
        {
            // 1. Die defekte aktuelle Version aus dem Weg räumen
            if (File.Exists(currentExe))
            {
                File.Move(currentExe, brokenBackup, overwrite: true);
            }

            // 2. Das Backup wieder als aktuelle Executable herstellen
            File.Move(backupPath, currentExe, overwrite: true);

            // 3. Verbleibende Kopie des Backups bereinigen
            var primaryBackup = Path.Combine(dir, $"{exeName}.previous.exe");
            var legacyBackup = Path.Combine(dir, $"{exeName}.old");
            try
            {
                if (backupPath == primaryBackup && File.Exists(legacyBackup)) File.Delete(legacyBackup);
                else if (backupPath == legacyBackup && File.Exists(primaryBackup)) File.Delete(primaryBackup);
            }
            catch { }

            // 4. Startup-Gesundheitsüberwachung zurücksetzen, damit kein Crash-Loop nach Rollback gemeldet wird
            try
            {
                new StartupHealthTracker().Reset();
            }
            catch { }

            if (restart)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = currentExe,
                    Arguments = "--after-rollback",
                    UseShellExecute = true,
                    WorkingDirectory = dir
                };
                Process.Start(startInfo);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
