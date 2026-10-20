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

    [Fact]
    public void DrawStepBadge_WhiteColor_ShouldDrawWithDarkContrastText()
    {
        // Transparent background so we can clearly see the badge and border/text
        using var source = CreateTestBitmap(100, 100, SKColors.Transparent);
        using var result = _service.DrawStepBadge(source, new SKPoint(50, 50), number: 1, SKColors.White, radius: 20f);

        result.Width.Should().Be(100);
        result.Height.Should().Be(100);

        // At center (where number 1 is drawn), text pixels should be dark (#0F172A)
        bool hasDarkTextPixel = false;
        for (int y = 40; y <= 60; y++)
        {
            for (int x = 40; x <= 60; x++)
            {
                var px = result.GetPixel(x, y);
                if (px.Red < 50 && px.Green < 50 && px.Blue < 60 && px.Alpha > 200)
                {
                    hasDarkTextPixel = true;
                    break;
                }
            }
            if (hasDarkTextPixel) break;
        }

        hasDarkTextPixel.Should().BeTrue("White badge must use dark contrast text instead of white-on-white.");
    }

    [Fact]
    public void ApplyBlurBrush_EmptyStroke_ShouldReturnCopy()
    {
        using var source = CreateTestBitmap(100, 100, SKColors.Red);
        using var result = _service.ApplyBlurBrush(source, Array.Empty<SKPoint>(), brushRadius: 20f, sigma: 10f);

        result.Width.Should().Be(100);
        result.Height.Should().Be(100);
        result.GetPixel(50, 50).Should().Be(SKColors.Red);
    }

    [Fact]
    public void ApplyBlurBrush_WithStroke_ShouldModifyPixelsAlongPath()
    {
        using var source = CreateTestBitmap(100, 100, SKColors.White);
        // Draw sharp high-contrast black box in middle
        using (var canvas = new SKCanvas(source))
        using (var paint = new SKPaint { Color = SKColors.Black })
        {
            canvas.DrawRect(30, 30, 40, 40, paint);
        }

        var stroke = new[] { new SKPoint(30, 50), new SKPoint(70, 50) };
        using var result = _service.ApplyBlurBrush(source, stroke, brushRadius: 15f, sigma: 8f);

        result.Width.Should().Be(100);
        result.Height.Should().Be(100);
        // Pixel at edge of black box (30, 50) should now be blurred / blended (neither pure white nor pure black)
        var edgePixel = result.GetPixel(30, 50);
        edgePixel.Should().NotBe(SKColors.White);
    }

    [Fact]
    public void RemoveBackgroundByColor_MatchingKeyColor_ShouldBecomeTransparent()
    {
        using var source = CreateTestBitmap(100, 100, SKColors.Green);
        // Paint red square in middle
        using (var canvas = new SKCanvas(source))
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(30, 30, 40, 40, paint);
        }

        using var result = _service.RemoveBackgroundByColor(source, SKColors.Green, tolerance: 0.15f);

        result.Width.Should().Be(100);
        result.Height.Should().Be(100);

        // Corner pixel should now be transparent
        var corner = result.GetPixel(5, 5);
        corner.Alpha.Should().Be(0);

        // Middle red pixel should be preserved and opaque
        var center = result.GetPixel(50, 50);
        center.Alpha.Should().Be(255);
        center.Red.Should().Be(255);
    }

    [Theory]
    [InlineData(1f, 12f)]
    [InlineData(4f, 18f)]
    [InlineData(16f, 42f)]
    [InlineData(64f, 138f)]
    public void DrawStepBadge_DifferentRadii_ShouldRenderProportionally(float strokeWidth, float expectedRadius)
    {
        float calculatedRadius = Math.Max(12f, 10f + strokeWidth * 2f);
        calculatedRadius.Should().Be(expectedRadius);

        using var source = CreateTestBitmap(300, 300, SKColors.White);
        using var result = _service.DrawStepBadge(source, new SKPoint(150, 150), number: 5, SKColors.Blue, radius: calculatedRadius);

        result.Width.Should().Be(300);
        // Point within badge radius should have badge color and not be background white
        int testX = 150 + (int)(calculatedRadius * 0.6f);
        result.GetPixel(testX, 150).Should().NotBe(SKColors.White);
    }

    [Fact]
    public void ResizeCanvas_TopLeftAnchor_PreservesOriginalContentAndFillsBackground()
    {
        using var source = CreateTestBitmap(100, 100, SKColors.Red);
        using var result = _service.ResizeCanvas(source, 200, 150, CanvasAnchor.TopLeft, SKColors.Transparent);

        result.Width.Should().Be(200);
        result.Height.Should().Be(150);

        // Top-left 100x100 should be red
        result.GetPixel(10, 10).Red.Should().Be(255);
        result.GetPixel(99, 99).Red.Should().Be(255);

        // Expanded area to the right should be transparent
        result.GetPixel(150, 50).Alpha.Should().Be(0);
        // Expanded area below should be transparent
        result.GetPixel(50, 120).Alpha.Should().Be(0);
    }

    [Fact]
    public void ResizeCanvas_CenterAnchor_CentersOriginalContent()
    {
        using var source = CreateTestBitmap(100, 100, SKColors.Blue);
        // Expand from 100x100 to 200x200 centered -> offset should be (50, 50)
        using var result = _service.ResizeCanvas(source, 200, 200, CanvasAnchor.Center, SKColors.White);

        result.Width.Should().Be(200);
        result.Height.Should().Be(200);

        // Center (100, 100) should be Blue
        var centerPixel = result.GetPixel(100, 100);
        centerPixel.Blue.Should().Be(255);
        centerPixel.Red.Should().Be(0);

        // Corner (10, 10) should be White
        var cornerPixel = result.GetPixel(10, 10);
        cornerPixel.Red.Should().Be(255);
        cornerPixel.Green.Should().Be(255);
        cornerPixel.Blue.Should().Be(255);
    }
}

