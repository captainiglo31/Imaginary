using System;
using System.IO;
using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using Xunit;

namespace Imaginary.Core.Tests;

public class StartMenuIntegrationTests
{
    [Fact]
    public void GetShortcutPath_OnWindows_ReturnsValidPathInProgramsFolder()
    {
        if (!OperatingSystem.IsWindows()) return;

        var service = new StartMenuIntegration();
        var shortcutPath = service.GetShortcutPath();

        shortcutPath.Should().NotBeNullOrWhiteSpace();
        shortcutPath.Should().EndWith("Imaginary.lnk");
        shortcutPath.Should().Contain(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
    }

    [Fact]
    public void Register_WithNonExistentPath_ReturnsFalse()
    {
        var service = new StartMenuIntegration();
        var result = service.Register(@"C:\non_existent_path_imaginary_test_12345\imaginary.exe");
        result.Should().BeFalse();
    }

    [Fact]
    public void AppSettings_StartMenuIntegrationEnabled_DefaultsToTrue()
    {
        var settings = new AppSettings();
        settings.StartMenuIntegrationEnabled.Should().BeTrue();
    }

    [Fact]
    public void Register_And_Unregister_CleansUpProperly()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tempExe = Path.Combine(Path.GetTempPath(), $"imaginary_test_{Guid.NewGuid():N}.exe");
        File.WriteAllText(tempExe, "dummy executable content");

        var service = new StartMenuIntegration();
        try
        {
            var registered = service.Register(tempExe);
            registered.Should().BeTrue();

            var shortcutPath = service.GetShortcutPath();
            File.Exists(shortcutPath).Should().BeTrue();

            service.IsRegistered().Should().BeTrue();
        }
        finally
        {
            service.Unregister();
            if (File.Exists(tempExe))
            {
                File.Delete(tempExe);
            }
        }

        File.Exists(service.GetShortcutPath()).Should().BeFalse();
    }

    [Fact]
    public void Synchronize_WhenFalse_RemovesRegistration()
    {
        if (!OperatingSystem.IsWindows()) return;

        var service = new StartMenuIntegration();
        service.Synchronize(false);
        File.Exists(service.GetShortcutPath()).Should().BeFalse();
    }
}
