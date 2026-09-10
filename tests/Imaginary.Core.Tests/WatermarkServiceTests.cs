using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class WatermarkServiceTests
{
    private readonly WatermarkService _watermarkService = new();

    [Fact]
    public void ApplyWatermark_WithTextWatermark_ShouldRenderWithoutError()
    {
        // Arrange
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
        }

        var options = new WatermarkOptions
        {
            Type = WatermarkType.Text,
            Text = "CONFIDENTIAL",
            Position = WatermarkPosition.Center,
            Opacity = 0.5f,
            FontSize = 24
        };

        // Act
        _watermarkService.ApplyWatermark(bitmap, options);

        // Assert
        bitmap.Width.Should().Be(400);
        bitmap.Height.Should().Be(300);
    }

    [Fact]
    public void ApplyWatermark_WithNullOrDisabled_ShouldReturnOriginalBitmap()
    {
        // Arrange
        using var bitmap = new SKBitmap(200, 200);

        // Act
        _watermarkService.ApplyWatermark(bitmap, null);

        // Assert
        bitmap.Width.Should().Be(200);
    }
}
