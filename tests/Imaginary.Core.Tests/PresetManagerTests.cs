using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using Xunit;

namespace Imaginary.Core.Tests;

public class PresetManagerTests
{
    [Fact]
    public void GetPresets_ShouldContainBuiltInPresets()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_presets_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var presetPath = Path.Combine(tempDir, "presets.json");

        try
        {
            var manager = new PresetManager(presetPath);
            var presets = manager.GetAllPresets();

            presets.Should().NotBeEmpty();
            presets.Should().Contain(p => p.Name.Contains("E-Mail"));
            presets.Should().Contain(p => p.Name.Contains("Web-Shop"));
            presets.Should().Contain(p => p.Name.Contains("Social Media"));
            presets.Should().Contain(p => p.Name.Contains("Archiv"));
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
    public void SaveCustomPreset_ShouldPersistAndBeReloadable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_presets_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var presetPath = Path.Combine(tempDir, "presets.json");

        try
        {
            var manager = new PresetManager(presetPath);
            var custom = new Preset
            {
                Name = "My Custom Preset",
                Description = "Custom description",
                Options = new ConversionOptions
                {
                    TargetFormat = ImageFormat.Webp,
                    DefaultQuality = 88
                }
            };

            manager.SaveUserPreset(custom);

            // Reload with a new manager instance
            var manager2 = new PresetManager(presetPath);
            var presets = manager2.GetAllPresets();

            var found = presets.FirstOrDefault(p => p.Name == "My Custom Preset");
            found.Should().NotBeNull();
            found!.Options.DefaultQuality.Should().Be(88);
            found.Options.TargetFormat.Should().Be(ImageFormat.Webp);
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
