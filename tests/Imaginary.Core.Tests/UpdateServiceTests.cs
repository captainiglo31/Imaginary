using FluentAssertions;
using Imaginary.Core.Services;

namespace Imaginary.Core.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("V2.5.3", 2, 5, 3)]
    [InlineData("1.0.4", 1, 0, 4)]
    [InlineData("v2.1", 2, 1, 0)]
    [InlineData("v3.0.0-preview.1", 3, 0, 0)]
    public void ParseVersionFromTag_ShouldCorrectlyParseValidTags(string tag, int major, int minor, int build)
    {
        var result = UpdateService.ParseVersionFromTag(tag);
        result.Should().NotBeNull();
        result!.Major.Should().Be(major);
        result.Minor.Should().Be(minor);
        result.Build.Should().Be(build);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid_tag")]
    public void ParseVersionFromTag_ShouldReturnNullForInvalidTags(string? tag)
    {
        var result = UpdateService.ParseVersionFromTag(tag);
        result.Should().BeNull();
    }

    [Fact]
    public void ParseReleaseJson_ShouldDetectNewerVersion()
    {
        var currentVersion = new Version(1, 0, 0);
        var service = new UpdateService(currentVersion: currentVersion);

        var json = """
        {
            "tag_name": "v1.1.0",
            "name": "Imaginary 1.1.0 - Großes Feature-Update",
            "body": "### Neuerungen\n- AVIF Support\n- Dark Mode",
            "html_url": "https://github.com/PinoWackers/Imaginary/releases/tag/v1.1.0",
            "published_at": "2026-09-10T12:00:00Z",
            "assets": [
                {
                    "name": "Imaginary.exe",
                    "size": 80123456,
                    "browser_download_url": "https://github.com/PinoWackers/Imaginary/releases/download/v1.1.0/Imaginary.exe"
                }
            ]
        }
        """;

        var updateInfo = service.ParseReleaseJson(json);

        updateInfo.IsUpdateAvailable.Should().BeTrue();
        updateInfo.LatestVersion.Should().Be(new Version(1, 1, 0));
        updateInfo.ReleaseTitle.Should().Be("Imaginary 1.1.0 - Großes Feature-Update");
        updateInfo.DownloadUrl.Should().Be("https://github.com/PinoWackers/Imaginary/releases/download/v1.1.0/Imaginary.exe");
        updateInfo.FileSizeBytes.Should().Be(80123456);
        updateInfo.FormattedFileSize.Should().Contain("MB");
    }

    [Fact]
    public void ParseReleaseJson_ShouldNotMarkUpdateAvailable_WhenVersionIsSameOrOlder()
    {
        var currentVersion = new Version(1, 2, 0);
        var service = new UpdateService(currentVersion: currentVersion);

        var json = """
        {
            "tag_name": "v1.2.0",
            "name": "Imaginary 1.2.0",
            "assets": [
                {
                    "name": "Imaginary.exe",
                    "size": 75000000,
                    "browser_download_url": "https://github.com/PinoWackers/Imaginary/releases/download/v1.2.0/Imaginary.exe"
                }
            ]
        }
        """;

        var updateInfo = service.ParseReleaseJson(json);

        updateInfo.IsUpdateAvailable.Should().BeFalse();
        updateInfo.LatestVersion.Should().Be(new Version(1, 2, 0));
    }

    [Fact]
    public void ParseReleaseJson_ShouldFallbackToZip_WhenNoDirectExeAssetExists()
    {
        var currentVersion = new Version(1, 0, 0);
        var service = new UpdateService(currentVersion: currentVersion);

        var json = """
        {
            "tag_name": "v1.5.0",
            "name": "Imaginary 1.5.0",
            "assets": [
                {
                    "name": "Imaginary-Portable-win-x64.zip",
                    "size": 65000000,
                    "browser_download_url": "https://github.com/PinoWackers/Imaginary/releases/download/v1.5.0/Imaginary-Portable-win-x64.zip"
                }
            ]
        }
        """;

        var updateInfo = service.ParseReleaseJson(json);

        updateInfo.IsUpdateAvailable.Should().BeTrue();
        updateInfo.DownloadUrl.Should().Be("https://github.com/PinoWackers/Imaginary/releases/download/v1.5.0/Imaginary-Portable-win-x64.zip");
    }

    [Fact]
    public void ApplyUpdateAndRestart_ShouldRenameCurrentExeAndPlaceNewExeWithoutExternalScripts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_test_update_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var currentExe = Path.Combine(tempDir, "Imaginary.exe");
            var downloadedNewExe = Path.Combine(tempDir, "Imaginary_New.exe");

            File.WriteAllText(currentExe, "Old Version 1.0.0");
            File.WriteAllText(downloadedNewExe, "New Version 1.0.1");

            var service = new UpdateService();

            var result = service.ApplyUpdateAndRestart(downloadedNewExe, currentExe, startProcess: false);

            result.Should().BeTrue();

            // The target executable path must now contain the NEW content
            File.Exists(currentExe).Should().BeTrue();
            File.ReadAllText(currentExe).Should().Be("New Version 1.0.1");

            // The old executable must have been moved to .old
            var oldExe = Path.Combine(tempDir, "Imaginary.old");
            File.Exists(oldExe).Should().BeTrue();
            File.ReadAllText(oldExe).Should().Be("Old Version 1.0.0");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void CanRollback_ShouldDetectBackupCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_test_rollback_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var currentExe = Path.Combine(tempDir, "Imaginary.exe");
            File.WriteAllText(currentExe, "Current Version 2.2.0");

            var service = new UpdateService();

            // No backup exists yet
            service.CanRollback(out var ver, out var path, currentExe).Should().BeFalse();
            path.Should().BeNull();

            // Create a backup file
            var prevExe = Path.Combine(tempDir, "Imaginary.previous.exe");
            File.WriteAllText(prevExe, "Previous Version 2.1.0");

            // Write metadata
            var metaPath = Path.Combine(tempDir, "Imaginary_backup_info.json");
            File.WriteAllText(metaPath, """{"previousVersion": "2.1.0"}""");

            service.CanRollback(out var foundVer, out var foundPath, currentExe).Should().BeTrue();
            foundVer.Should().Be("2.1.0");
            foundPath.Should().Be(prevExe);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void RollbackToPreviousVersion_ShouldRestoreBackupAndMarkBroken()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_test_rollback_exec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var currentExe = Path.Combine(tempDir, "Imaginary.exe");
            var prevExe = Path.Combine(tempDir, "Imaginary.previous.exe");

            File.WriteAllText(currentExe, "Broken Version 2.2.0");
            File.WriteAllText(prevExe, "Working Version 2.1.0");

            var service = new UpdateService();

            var result = service.RollbackToPreviousVersion(restart: false, targetExecutablePath: currentExe);
            result.Should().BeTrue();

            // currentExe should now contain the restored working version
            File.Exists(currentExe).Should().BeTrue();
            File.ReadAllText(currentExe).Should().Be("Working Version 2.1.0");

            // previousExe should no longer exist (it was moved to currentExe)
            File.Exists(prevExe).Should().BeFalse();

            // A broken backup should have been created
            var brokenFiles = Directory.GetFiles(tempDir, "Imaginary.broken_*.old");
            brokenFiles.Should().NotBeEmpty();
            File.ReadAllText(brokenFiles[0]).Should().Be("Broken Version 2.2.0");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}

