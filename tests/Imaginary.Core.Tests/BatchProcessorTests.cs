using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using Imaginary.Core.Tests.TestHelpers;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class BatchProcessorTests
{
    private readonly BatchProcessor _batchProcessor;

    public BatchProcessorTests()
    {
        var detector = new ImageFormatDetector();
        var converter = new ImageConverter(detector);
        var resizer = new ImageResizer();
        var quantizer = new OctreeQuantizer();
        var solver = new SizeConstraintSolver(converter, resizer, quantizer, detector);
        var pathResolver = new OutputPathResolver(detector);
        var watermark = new WatermarkService();

        _batchProcessor = new BatchProcessor(detector, converter, resizer, solver, pathResolver, watermark);
    }

    [Fact]
    public async Task ProcessBatchAsync_WithCorruptedFileInBatch_ShouldProcessOthersAndReportError()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_batch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var validPng = TestImageGenerator.CreateSolidImage(100, 100, SKColors.Green, ImageFormat.Png);
            var validJpg = TestImageGenerator.CreateSolidImage(100, 100, SKColors.Yellow, ImageFormat.Jpeg);
            var corruptedBytes = new byte[] { 0x00, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC }; // Corrupted image data

            var inputs = new List<ImageJobInput>
            {
                ImageJobInput.FromBytes("valid1.png", validPng),
                ImageJobInput.FromBytes("broken.jpg", corruptedBytes),
                ImageJobInput.FromBytes("valid2.jpg", validJpg)
            };

            var options = new ConversionOptions
            {
                TargetFormat = ImageFormat.Webp,
                OutputDirectory = tempDir
            };

            var reportedProgress = new List<BatchProgress>();
            var progress = new Progress<BatchProgress>(p => reportedProgress.Add(p));

            // Act
            var batchResult = await _batchProcessor.ProcessBatchAsync(inputs, options, saveToDisk: true, progress: progress);

            // Assert
            batchResult.TotalCount.Should().Be(3);
            batchResult.SucceededCount.Should().Be(2);
            batchResult.FailedCount.Should().Be(1);

            var failedJob = batchResult.Results.Single(r => !r.Success);
            failedJob.FileName.Should().Be("broken.jpg");
            failedJob.ErrorMessage.Should().NotBeNullOrWhiteSpace();

            var successfulJobs = batchResult.Results.Where(r => r.Success).ToList();
            successfulJobs.Should().HaveCount(2);
            successfulJobs.All(j => j.FinalFormat == ImageFormat.Webp).Should().BeTrue();
            successfulJobs.All(j => File.Exists(j.TargetPath)).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ProcessBatchAsync_HalveResolutionScenario_ShouldScaleAllImages()
    {
        // Arrange: PLAN.md Scenario 3: "Ganzer Ordner -> alle Bilder in Pixel-Auflösung halbieren, Format bleibt gleich"
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_batch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var img1 = TestImageGenerator.CreateSolidImage(200, 100, SKColors.Red, ImageFormat.Png);
            var img2 = TestImageGenerator.CreateSolidImage(400, 300, SKColors.Blue, ImageFormat.Jpeg);

            var inputs = new List<ImageJobInput>
            {
                ImageJobInput.FromBytes("img1.png", img1),
                ImageJobInput.FromBytes("img2.jpg", img2)
            };

            var options = new ConversionOptions
            {
                TargetFormat = null, // Keep original format
                ResizeMode = ResizeMode.Percentage,
                ResizePercentage = 50.0,
                OutputDirectory = tempDir
            };

            // Act
            var batchResult = await _batchProcessor.ProcessBatchAsync(inputs, options, saveToDisk: true);

            // Assert
            batchResult.SucceededCount.Should().Be(2);

            var r1 = batchResult.Results.Single(r => r.FileName == "img1.png");
            r1.FinalFormat.Should().Be(ImageFormat.Png);
            r1.FinalDimensions!.Value.Width.Should().Be(100);
            r1.FinalDimensions!.Value.Height.Should().Be(50);

            var r2 = batchResult.Results.Single(r => r.FileName == "img2.jpg");
            r2.FinalFormat.Should().Be(ImageFormat.Jpeg);
            r2.FinalDimensions!.Value.Width.Should().Be(200);
            r2.FinalDimensions!.Value.Height.Should().Be(150);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
