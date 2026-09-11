using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using SkiaSharp;

namespace Imaginary.Core.Tests;

public class ImageEditorServiceTests
{
    private readonly ImageEditorService _service = new();

    private static SKBitmap CreateTestBitmap(int width = 100, int height = 100, SKColor? color = null)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(color ?? SKColors.White);
        return bmp;
    }

    [Fact]
    public void Crop_ShouldReturnCorrectDimensions()
    {
        using var source = CreateTestBitmap(200, 200);
        var cropRect = new SKRectI(20, 30, 120, 150); // width 100, height 120

        using var cropped = _service.Crop(source, cropRect);

        cropped.Width.Should().Be(100);
        cropped.Height.Should().Be(120);
    }

    [Fact]
    public void Crop_ClampedOutOfBounds_ShouldNotThrow()
    {
        using var source = CreateTestBitmap(100, 100);
        var cropRect = new SKRectI(-50, -50, 300, 300);

        using var cropped = _service.Crop(source, cropRect);

        cropped.Width.Should().Be(100);
        cropped.Height.Should().Be(100);
    }

    [Fact]
    public void ApplyPixelate_ShouldModifyTargetRegion()
    {
        using var source = CreateTestBitmap(100, 100, SKColors.Red);
        // Draw a green square in the middle
        using (var canvas = new SKCanvas(source))
        using (var paint = new SKPaint { Color = SKColors.Green })
        {
            canvas.DrawRect(30, 30, 40, 40, paint);
        }

        var rect = new SKRectI(20, 20, 80, 80);
        using var pixelated = _service.ApplyPixelate(source, rect, pixelSize: 10);

        pixelated.Width.Should().Be(100);
        pixelated.Height.Should().Be(100);
        // Ensure outside is intact (Red)
        pixelated.GetPixel(5, 5).Should().Be(SKColors.Red);
    }

    [Fact]
    public void ApplyBlackout_ShouldDrawSolidColor()
    {
        using var source = CreateTestBitmap(100, 100, SKColors.White);
        var rect = new SKRectI(10, 10, 50, 50);

        using var result = _service.ApplyBlackout(source, rect, SKColors.Black);

        result.GetPixel(25, 25).Should().Be(SKColors.Black);
        result.GetPixel(5, 5).Should().Be(SKColors.White);
    }

    [Fact]
    public void RemoveBackgroundByColor_WhiteBackground_ShouldBecomeTransparent()
    {
        using var source = CreateTestBitmap(50, 50, SKColors.White);
        // Draw blue subject in center
        using (var canvas = new SKCanvas(source))
        using (var paint = new SKPaint { Color = SKColors.Blue })
        {
            canvas.DrawCircle(25, 25, 10, paint);
        }

        using var transparentBmp = _service.RemoveBackgroundByColor(source, SKColors.White, tolerance: 0.15f);

        // Corner (was white) should now have Alpha = 0
        transparentBmp.GetPixel(0, 0).Alpha.Should().Be(0);

        // Center (blue) should remain opaque
        transparentBmp.GetPixel(25, 25).Alpha.Should().Be(255);
        transparentBmp.GetPixel(25, 25).Blue.Should().Be(255);
    }

    [Fact]
    public void DrawArrow_ShouldDrawWithoutError()
    {
        using var source = CreateTestBitmap(200, 200, SKColors.White);
        using var result = _service.DrawArrow(source, new SKPoint(20, 20), new SKPoint(150, 150), SKColors.Red, strokeWidth: 4f);

        result.Width.Should().Be(200);
        result.Height.Should().Be(200);
        // Point along line shouldn't be pure white anymore
        result.GetPixel(20, 20).Should().NotBe(SKColors.White);
    }

    [Fact]
    public void DrawStepBadge_ShouldDrawCircularBadgeWithNumber()
    {
        using var source = CreateTestBitmap(200, 200, SKColors.White);
        using var result = _service.DrawStepBadge(source, new SKPoint(50, 50), number: 1, SKColors.Red, radius: 20f);

        result.Width.Should().Be(200);
        // Point within badge radius should have badge color
        result.GetPixel(60, 50).Should().NotBe(SKColors.White);
    }
}
