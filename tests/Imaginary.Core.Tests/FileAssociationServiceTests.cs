using System;
using System.IO;
using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using Xunit;

namespace Imaginary.Core.Tests;

public class FileAssociationServiceTests
{
    [Fact]
    public void AppSettings_DefaultFileOpenAction_DefaultsToAddToBatchList()
    {
        var settings = new AppSettings();
        settings.DefaultFileOpenAction.Should().Be(DefaultFileOpenAction.AddToBatchList);
        settings.FileAssociationsRegistered.Should().BeFalse();
    }

    [Fact]
    public void Register_WithNonExistentPath_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows()) return;

        var service = new FileAssociationService();
        var result = service.Register(@"C:\non_existent_imaginary_test_9999\imaginary.exe");
        result.Should().BeFalse();
    }

    [Fact]
    public void Register_And_Unregister_CleansUpProperly()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tempExe = Path.Combine(Path.GetTempPath(), $"imaginary_assoc_test_{Guid.NewGuid():N}.exe");
        File.WriteAllText(tempExe, "dummy test executable");

        var service = new FileAssociationService();
        try
        {
            var registered = service.Register(tempExe);
            registered.Should().BeTrue();
            service.IsRegistered().Should().BeTrue();
        }
        finally
        {
            var unregistered = service.Unregister();
            unregistered.Should().BeTrue();
            service.IsRegistered().Should().BeFalse();

            if (File.Exists(tempExe))
            {
                File.Delete(tempExe);
            }
        }
    }

    [Fact]
    public void ExplorerIntegration_Register_And_Unregister_CleansUpProperly()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tempExe = Path.Combine(Path.GetTempPath(), $"imaginary_exp_test_{Guid.NewGuid():N}.exe");
        File.WriteAllText(tempExe, "dummy test executable");

        var service = new ExplorerIntegration();
        try
        {
            var registered = service.Register(tempExe);
            registered.Should().BeTrue();
            service.IsRegistered().Should().BeTrue();
        }
        finally
        {
            service.Unregister();
            service.IsRegistered().Should().BeFalse();

            if (File.Exists(tempExe))
            {
                File.Delete(tempExe);
            }
        }
    }
}
