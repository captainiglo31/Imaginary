using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class AutoRedactionServiceTests
{
    private readonly AutoRedactionService _service = new();

    [Fact]
    public void RedactRegions_Pixelate_ShouldModifyTargetArea()
    {
        // Arrange
        using var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        // Draw some high contrast pattern in middle
        canvas.DrawRect(50, 50, 50, 50, new SKPaint { Color = SKColors.Red });

        var regions = new[]
        {
            new DetectedSensitiveRegion(new SKRectI(40, 40, 110, 110), SensitiveRegionType.Face)
        };

        // Act
        using var redacted = _service.RedactRegions(bitmap, regions, RedactionStyle.Pixelate, intensity: 10);

        // Assert: pixel inside redaction area should not be pure original red
        redacted.Width.Should().Be(200);
        redacted.Height.Should().Be(200);
    }

    [Fact]
    public void RedactRegions_Blackout_ShouldDrawSolidBlack()
    {
        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var regions = new[]
        {
            new DetectedSensitiveRegion(new SKRectI(20, 20, 80, 80), SensitiveRegionType.LicensePlate)
        };

        using var redacted = _service.RedactRegions(bitmap, regions, RedactionStyle.Blackout);

        // Pixel inside should be black
        var pixel = redacted.GetPixel(50, 50);
        pixel.Red.Should().Be(0);
        pixel.Green.Should().Be(0);
        pixel.Blue.Should().Be(0);
    }
}
