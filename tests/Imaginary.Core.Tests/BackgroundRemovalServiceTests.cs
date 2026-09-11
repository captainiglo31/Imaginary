using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using SkiaSharp;

namespace Imaginary.Core.Tests;

public class BackgroundRemovalServiceTests
{
    private readonly ImageEditorService _editorService = new();
    private readonly BackgroundRemovalService _bgService;

    public BackgroundRemovalServiceTests()
    {
        _bgService = new BackgroundRemovalService(_editorService);
    }

    private static SKBitmap CreateBitmap(int width, int height, SKColor color)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(color);
        return bmp;
    }

    [Fact]
    public void ApplyMaskBrush_Erase_ShouldReduceAlphaInRadius()
    {
        using var current = CreateBitmap(100, 100, SKColors.Blue);
        using var original = CreateBitmap(100, 100, SKColors.Blue);

        var center = new SKPoint(50, 50);
        float radius = 20f;

        var result = _bgService.ApplyMaskBrush(current, original, center, radius, restore: false);

        // Center pixel should now have alpha 0 (erased)
        var centerPx = result.GetPixel(50, 50);
        centerPx.Alpha.Should().Be(0);

        // Pixel outside radius should remain fully opaque
        var outsidePx = result.GetPixel(10, 10);
        outsidePx.Alpha.Should().Be(255);
        outsidePx.Blue.Should().Be(255);
    }

    [Fact]
    public void ApplyMaskBrush_Restore_ShouldRestoreAlphaAndColorFromOriginal()
    {
        using var original = CreateBitmap(100, 100, SKColors.Red);
        using var current = CreateBitmap(100, 100, SKColors.Transparent);

        current.GetPixel(50, 50).Alpha.Should().Be(0);

        var center = new SKPoint(50, 50);
        float radius = 15f;

        var result = _bgService.ApplyMaskBrush(current, original, center, radius, restore: true);

        // Center pixel should now be restored to original Red and opaque
        var restoredPx = result.GetPixel(50, 50);
        restoredPx.Alpha.Should().Be(255);
        restoredPx.Red.Should().Be(255);

        // Outside pixel remains transparent
        result.GetPixel(10, 10).Alpha.Should().Be(0);
    }

    [Fact]
    public void ApplyMaskBrush_OutOfBoundsPoint_ShouldNotThrow()
    {
        using var current = CreateBitmap(50, 50, SKColors.Green);
        using var original = CreateBitmap(50, 50, SKColors.Green);

        // Point near/outside edge
        var edgePoint = new SKPoint(-10, -10);
        var act = () => _bgService.ApplyMaskBrush(current, original, edgePoint, radius: 25f, restore: false);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task SegmentObjectRegionAsync_WithoutModel_ThrowsInvalidOperationException()
    {
        using var source = CreateBitmap(100, 100, SKColors.Yellow);
        var roi = new SKRectI(20, 20, 80, 80);

        if (!_bgService.IsAiModelDownloaded())
        {
            var act = async () => await _bgService.SegmentObjectRegionAsync(source, roi);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
