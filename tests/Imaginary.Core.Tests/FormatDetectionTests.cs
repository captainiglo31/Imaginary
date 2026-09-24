using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using Imaginary.Core.Tests.TestHelpers;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class FormatDetectionTests
{
    private readonly ImageFormatDetector _detector = new();

    [Theory]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.Png)]
    [InlineData(ImageFormat.Webp)]
    [InlineData(ImageFormat.Gif)]
    [InlineData(ImageFormat.Bmp)]
    public void DetectFormat_FromBytes_ShouldIdentifyCorrectFormat(ImageFormat expectedFormat)
    {
        // Arrange
        var bytes = TestImageGenerator.CreateSolidImage(50, 50, SKColors.Blue, expectedFormat);

        // Act
        var detected = _detector.DetectFormat(bytes);

        // Assert
        detected.Should().Be(expectedFormat);
    }

    [Fact]
    public void DetectFormat_WithMisleadingExtension_ShouldDetectActualBytesFormat()
    {
        // Arrange: A valid PNG file saved with .jpg extension
        var pngBytes = TestImageGenerator.CreateSolidImage(50, 50, SKColors.Red, ImageFormat.Png);
        var tempFile = Path.Combine(Path.GetTempPath(), $"fake_jpeg_{Guid.NewGuid():N}.jpg");

        try
        {
            File.WriteAllBytes(tempFile, pngBytes);

            // Act
            var detected = _detector.DetectFormat(tempFile);

            // Assert: Despite the .jpg extension, it must be detected as PNG!
            detected.Should().Be(ImageFormat.Png);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFromExtension_ShouldMapKnownExtensions()
    {
        _detector.DetectFromExtension("photo.jpg").Should().Be(ImageFormat.Jpeg);
        _detector.DetectFromExtension("image.PNG").Should().Be(ImageFormat.Png);
        _detector.DetectFromExtension("anim.gif").Should().Be(ImageFormat.Gif);
        _detector.DetectFromExtension("icon.webp").Should().Be(ImageFormat.Webp);
        _detector.DetectFromExtension("bitmap.bmp").Should().Be(ImageFormat.Bmp);
        _detector.DetectFromExtension("scan.tiff").Should().Be(ImageFormat.Tiff);
        _detector.DetectFromExtension("iphone.heic").Should().Be(ImageFormat.Heic);
        _detector.DetectFromExtension("livephoto.HEIF").Should().Be(ImageFormat.Heic);
        _detector.DetectFromExtension("document.pdf").Should().Be(ImageFormat.Unknown);
    }

    [Fact]
    public void DetectFormat_HeicHeader_ShouldDetectHeic()
    {
        // 00 00 00 18 'ftyp' 'heic'
        var heicHeader = new byte[]
        {
            0x00, 0x00, 0x00, 0x18,
            (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'h', (byte)'e', (byte)'i', (byte)'c',
            0x00, 0x00, 0x00, 0x00
        };

        _detector.DetectFormat(heicHeader).Should().Be(ImageFormat.Heic);
        _detector.GetDefaultExtension(ImageFormat.Heic).Should().Be(".heic");
        _detector.GetMimeType(ImageFormat.Heic).Should().Be("image/heic");
    }

    [Fact]
    public void IsLossy_ShouldCorrectlyClassifyFormats()
    {
        _detector.IsLossy(ImageFormat.Jpeg).Should().BeTrue();
        _detector.IsLossy(ImageFormat.Webp).Should().BeTrue();
        _detector.IsLossy(ImageFormat.Heic).Should().BeTrue();
        _detector.IsLossy(ImageFormat.Png).Should().BeFalse();
        _detector.IsLossy(ImageFormat.Bmp).Should().BeFalse();
        _detector.IsLossy(ImageFormat.Gif).Should().BeFalse();
    }
}
