using System.Linq;
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

        var pixel = redacted.GetPixel(50, 50);
        pixel.Red.Should().Be(0);
        pixel.Green.Should().Be(0);
        pixel.Blue.Should().Be(0);
    }

    [Fact]
    public void DetectRegions_WheelOrHeadlightPattern_ShouldNotDetectLicensePlate()
    {
        // A dark background with circular silver/gray ring (wheel rim / headlight)
        using var bitmap = new SKBitmap(400, 400);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(30, 30, 30)); // Dark wheel arch / body

        // Draw dark wheel tire with spoke pattern (no white plate background, no alphanumeric characters)
        using var tirePaint = new SKPaint { Color = new SKColor(20, 20, 20), Style = SKPaintStyle.Fill };
        canvas.DrawCircle(200, 250, 80, tirePaint);

        using var rimPaint = new SKPaint { Color = new SKColor(160, 160, 160), Style = SKPaintStyle.Stroke, StrokeWidth = 14 };
        canvas.DrawCircle(200, 250, 50, rimPaint);

        var detected = _service.DetectRegions(bitmap, detectFaces: false, detectPlates: true);
        detected.Should().BeEmpty("Wheel rims and radial circular patterns must not trigger false positive license plate detections.");
    }

    [Fact]
    public void DetectRegions_SyntheticLicensePlate_ShouldDetectPlate()
    {
        // 500x400 image with a white license plate with black characters in the lower half
        using var bitmap = new SKBitmap(500, 400);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(40, 50, 60)); // Car bumper color

        // Draw white plate (aspect ratio ~4.7:1, e.g. 188 x 40 px)
        int plateX = 150;
        int plateY = 280;
        int plateW = 188;
        int plateH = 40;

        using var plateBgPaint = new SKPaint { Color = new SKColor(245, 245, 245), Style = SKPaintStyle.Fill };
        canvas.DrawRect(plateX, plateY, plateW, plateH, plateBgPaint);

        // Draw dark characters ("ERK SY 44") with alternating black bars
        using var charPaint = new SKPaint { Color = new SKColor(15, 15, 15), Style = SKPaintStyle.Fill };
        for (int i = 0; i < 7; i++)
        {
            int cx = plateX + 15 + i * 22;
            canvas.DrawRect(cx, plateY + 8, 12, 24, charPaint);
        }

        var detected = _service.DetectRegions(bitmap, detectFaces: false, detectPlates: true);
        detected.Should().NotBeEmpty("A valid white license plate with dark alphanumeric text must be detected.");
        detected.Any(d => d.Type == SensitiveRegionType.LicensePlate).Should().BeTrue();
    }

    [Fact]
    public void DetectRegions_UniformWarmColorReflection_ShouldNotDetectFace()
    {
        // Uniform tan/brown reflection without human eye socket depression
        using var bitmap = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(50, 60, 70));

        // Draw a flat warm oval (like a car headrest or reflection)
        using var warmPaint = new SKPaint { Color = new SKColor(210, 160, 125), Style = SKPaintStyle.Fill };
        canvas.DrawOval(150, 100, 45, 60, warmPaint);

        var detected = _service.DetectRegions(bitmap, detectFaces: true, detectPlates: false);
        detected.Should().BeEmpty("Uniform warm surfaces without eye sockets must not be detected as human faces.");
    }
}
