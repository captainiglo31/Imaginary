using FluentAssertions;
using Imaginary.Core.Services;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class UpscaleServiceTests
{
    private readonly UpscaleService _service = new();

    [Theory]
    [InlineData(100, 100, 2, 200, 200)]
    [InlineData(150, 100, 2, 300, 200)]
    [InlineData(64, 64, 4, 256, 256)]
    public void UpscaleHighQuality_ShouldMultiplyDimensionsAccurately(int origW, int origH, int factor, int expectedW, int expectedH)
    {
        using var source = new SKBitmap(origW, origH);
        using var canvas = new SKCanvas(source);
        canvas.Clear(SKColors.CornflowerBlue);

        using var result = _service.UpscaleHighQuality(source, factor);

        result.Width.Should().Be(expectedW);
        result.Height.Should().Be(expectedH);
    }

    [Fact]
    public async Task UpscaleAsync_WithoutModel_ShouldUseHighQualityFallback()
    {
        using var source = new SKBitmap(50, 50);
        using var canvas = new SKCanvas(source);
        canvas.Clear(SKColors.LimeGreen);

        using var result = await _service.UpscaleAsync(source, factor: 2);

        result.Width.Should().Be(100);
        result.Height.Should().Be(100);
    }
}
