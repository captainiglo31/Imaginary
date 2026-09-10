using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using Imaginary.Core.Tests.TestHelpers;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class SizeConstraintSolverTests
{
    private readonly SizeConstraintSolver _solver;
    private readonly ImageConverter _converter;
    private readonly ImageResizer _resizer;
    private readonly OctreeQuantizer _quantizer;
    private readonly ImageFormatDetector _detector;

    public SizeConstraintSolverTests()
    {
        _detector = new ImageFormatDetector();
        _converter = new ImageConverter(_detector);
        _resizer = new ImageResizer();
        _quantizer = new OctreeQuantizer();
        _solver = new SizeConstraintSolver(_converter, _resizer, _quantizer, _detector);
    }

    [Fact]
    public void Solve_JpegWith500KbLimit_ShouldResultInFileUnder500Kb()
    {
        // Arrange: Generate a high-entropy 1200x1200 image that is normally ~1MB+ as high-quality JPEG
        var imageBytes = TestImageGenerator.CreatePatternImage(1200, 1200, SKEncodedImageFormat.Jpeg, 100);
        var (bitmap, _) = _converter.LoadBitmap(imageBytes);

        var targetMaxBytes = 500 * 1024; // 0.5 MB (PLAN.md Scenario 1)
        var options = new ConversionOptions
        {
            TargetFormat = ImageFormat.Jpeg,
            MaxFileSizeInBytes = targetMaxBytes,
            DefaultQuality = 90
        };

        // Act
        using (bitmap)
        {
            var result = _solver.Solve(bitmap, ImageFormat.Jpeg, options);

            // Assert
            result.EncodedData.Length.Should().BeLessThanOrEqualTo(targetMaxBytes);
            result.Warnings.Should().BeEmpty();
        }
    }

    [Fact]
    public void Solve_PngWithResizeDownFallback_ShouldReduceDimensionsAndMeetSize()
    {
        // Arrange: Generate a 1000x1000 image
        var imageBytes = TestImageGenerator.CreatePatternImage(1000, 1000, SKEncodedImageFormat.Png);
        var (bitmap, _) = _converter.LoadBitmap(imageBytes);

        // Set a strict max size that is smaller than the original PNG
        var targetMaxBytes = imageBytes.Length / 4;
        var options = new ConversionOptions
        {
            TargetFormat = ImageFormat.Png,
            MaxFileSizeInBytes = targetMaxBytes,
            FallbackStrategy = FallbackStrategy.ResizeDown
        };

        // Act
        using (bitmap)
        {
            var result = _solver.Solve(bitmap, ImageFormat.Png, options);

            // Assert
            result.EncodedData.Length.Should().BeLessThanOrEqualTo(targetMaxBytes);
            result.FinalDimensions.Width.Should().BeLessThan(1000);
            result.FinalDimensions.Height.Should().BeLessThan(1000);
        }
    }

    [Fact]
    public void Solve_PngWithWarnOnly_ShouldKeepFullResolutionAndEmitWarning()
    {
        // Arrange
        var imageBytes = TestImageGenerator.CreatePatternImage(500, 500, SKEncodedImageFormat.Png);
        var (bitmap, _) = _converter.LoadBitmap(imageBytes);

        var strictLimit = 1000; // Intentionally impossible for 500x500 complex PNG without resize
        var options = new ConversionOptions
        {
            TargetFormat = ImageFormat.Png,
            MaxFileSizeInBytes = strictLimit,
            FallbackStrategy = FallbackStrategy.WarnOnly
        };

        // Act
        using (bitmap)
        {
            var result = _solver.Solve(bitmap, ImageFormat.Png, options);

            // Assert
            result.EncodedData.Length.Should().BeGreaterThan(strictLimit);
            result.Warnings.Should().Contain(w => w.Contains("limit"));
            result.FinalDimensions.Width.Should().Be(500);
            result.FinalDimensions.Height.Should().Be(500);
        }
    }
}
