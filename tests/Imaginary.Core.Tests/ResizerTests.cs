using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class ResizerTests
{
    private readonly ImageResizer _resizer = new();

    [Fact]
    public void Resize_WithPercentage50_ShouldHalvePixelDimensions()
    {
        // Arrange: 800x600 image (PLAN.md Scenario 3)
        using var original = new SKBitmap(800, 600);
        var options = new ConversionOptions
        {
            ResizeMode = ResizeMode.Percentage,
            ResizePercentage = 50.0
        };

        // Act
        using var resized = _resizer.Resize(original, options);

        // Assert
        resized.Width.Should().Be(400);
        resized.Height.Should().Be(300);
    }

    [Fact]
    public void Resize_WithAbsolutePixelsAndAspectRatio_ShouldFitWithinBounds()
    {
        // Arrange: 1920x1080 (16:9) image, target 800x800
        using var original = new SKBitmap(1920, 1080);
        var options = new ConversionOptions
        {
            ResizeMode = ResizeMode.AbsolutePixels,
            TargetWidth = 800,
            TargetHeight = 800,
            MaintainAspectRatio = true
        };

        // Act
        using var resized = _resizer.Resize(original, options);

        // Assert: Aspect ratio 16:9 within 800x800 -> 800 x 450
        resized.Width.Should().Be(800);
        resized.Height.Should().Be(450);
    }

    [Fact]
    public void Resize_WithAbsolutePixelsNoAspectRatio_ShouldUseExactDimensions()
    {
        // Arrange
        using var original = new SKBitmap(100, 100);
        var options = new ConversionOptions
        {
            ResizeMode = ResizeMode.AbsolutePixels,
            TargetWidth = 300,
            TargetHeight = 150,
            MaintainAspectRatio = false
        };

        // Act
        using var resized = _resizer.Resize(original, options);

        // Assert
        resized.Width.Should().Be(300);
        resized.Height.Should().Be(150);
    }

    [Fact]
    public void Resize_WithModeNone_ShouldMaintainDimensions()
    {
        // Arrange
        using var original = new SKBitmap(640, 480);
        var options = new ConversionOptions
        {
            ResizeMode = ResizeMode.None
        };

        // Act
        using var resized = _resizer.Resize(original, options);

        // Assert
        resized.Width.Should().Be(640);
        resized.Height.Should().Be(480);
    }

    [Fact]
    public void Resize_WithFillCrop_ShouldCropToTargetDimensions()
    {
        // Arrange: 1000x500 (2:1), target 500x500 (1:1)
        using var original = new SKBitmap(1000, 500);
        var options = new ConversionOptions
        {
            ResizeMode = ResizeMode.FillCrop,
            TargetWidth = 500,
            TargetHeight = 500
        };

        // Act
        using var resized = _resizer.Resize(original, options);

        // Assert
        resized.Width.Should().Be(500);
        resized.Height.Should().Be(500);
    }

    [Fact]
    public void Resize_WithPad_ShouldFitAndPadToTargetDimensions()
    {
        // Arrange: 800x400 (2:1), target 800x800 with padding
        using var original = new SKBitmap(800, 400);
        var options = new ConversionOptions
        {
            ResizeMode = ResizeMode.Pad,
            TargetWidth = 800,
            TargetHeight = 800,
            PadColor = "#000000"
        };

        // Act
        using var resized = _resizer.Resize(original, options);

        // Assert
        resized.Width.Should().Be(800);
        resized.Height.Should().Be(800);
    }

    [Fact]
    public void Resize_WithMaxEdge_ShouldScaleProportionalToLongestEdge()
    {
        // Arrange: 1200x600, MaxEdge 600
        using var original = new SKBitmap(1200, 600);
        var options = new ConversionOptions
        {
            ResizeMode = ResizeMode.MaxEdge,
            MaxEdgeLength = 600
        };

        // Act
        using var resized = _resizer.Resize(original, options);

        // Assert: 1200->600, 600->300
        resized.Width.Should().Be(600);
        resized.Height.Should().Be(300);
    }
}
