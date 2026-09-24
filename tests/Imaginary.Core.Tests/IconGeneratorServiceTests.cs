using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class IconGeneratorServiceTests
{
    private readonly IconGeneratorService _service = new();

    [Fact]
    public void GetAvailableTemplates_ShouldCoverAllPlatforms()
    {
        var templates = _service.GetAvailableTemplates();
        templates.Should().NotBeEmpty();

        var platforms = templates.Select(t => t.Platform).Distinct().ToList();
        platforms.Should().Contain(IconTargetPlatform.Windows);
        platforms.Should().Contain(IconTargetPlatform.WebFavicon);
        platforms.Should().Contain(IconTargetPlatform.AppleIosMac);
        platforms.Should().Contain(IconTargetPlatform.Android);
    }

    [Fact]
    public void RenderSingleIcon_Square_ShouldMatchTargetDimensions()
    {
        using var source = new SKBitmap(500, 300);
        using var rendered = _service.RenderSingleIcon(source, 180, 180, IconShape.Rounded, paddingPercent: 0.1f);

        rendered.Width.Should().Be(180);
        rendered.Height.Should().Be(180);
    }

    [Fact]
    public void GenerateIconPackage_ShouldCreateCompletePackageWithZip()
    {
        using var source = new SKBitmap(256, 256);
        var options = new IconGenerationOptions
        {
            BaseName = "TestApp",
            Shape = IconShape.Rounded
        };

        var result = _service.GenerateIconPackage(source, options);
        result.Files.Should().NotBeEmpty();

        // Check essential files
        result.Files.Should().Contain(f => f.RelativePath.EndsWith("icon.ico"));
        result.Files.Should().Contain(f => f.RelativePath.EndsWith("favicon.ico"));
        result.Files.Should().Contain(f => f.RelativePath.EndsWith("site.webmanifest"));
        result.Files.Should().Contain(f => f.RelativePath.EndsWith("AppIcon-1024.png"));

        // ZIP creation should succeed
        var zipBytes = result.CreateZipArchive();
        zipBytes.Should().NotBeNull();
        zipBytes.Length.Should().BeGreaterThan(100);
    }
}
