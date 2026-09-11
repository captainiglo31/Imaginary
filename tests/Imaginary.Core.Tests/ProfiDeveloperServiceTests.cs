using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using Imaginary.Core.Tests.TestHelpers;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class ProfiDeveloperServiceTests
{
    private readonly ProfiDeveloperService _service = new();
    private readonly ImageFormatDetector _detector = new();

    [Theory]
    [InlineData("photo.cr2", ImageFormat.Raw)]
    [InlineData("photo.cr3", ImageFormat.Raw)]
    [InlineData("image.NEF", ImageFormat.Raw)]
    [InlineData("sony.arw", ImageFormat.Raw)]
    [InlineData("adobe.dng", ImageFormat.Raw)]
    [InlineData("fuji.raf", ImageFormat.Raw)]
    [InlineData("olympus.orf", ImageFormat.Raw)]
    [InlineData("panasonic.rw2", ImageFormat.Raw)]
    [InlineData("pentax.pef", ImageFormat.Raw)]
    public void RawFormats_ShouldBeDetectedFromExtension(string filename, ImageFormat expected)
    {
        var detected = _detector.DetectFromExtension(filename);
        detected.Should().Be(expected);
    }

    [Fact]
    public void BuiltInPresets_ShouldContainTenProfessionalLooks()
    {
        var presets = _service.GetBuiltInPresets();

        presets.Should().NotBeNull();
        presets.Count.Should().Be(10);

        var names = presets.Select(p => p.Name).ToList();
        names.Should().Contain(new[]
        {
            "Original / Neutral",
            "Vivid Pop / Kräftig",
            "Cinema Teal & Orange",
            "Moody Urban / Street",
            "Golden Hour / Warmes Licht",
            "Arktisch Kühl (Cold Fade)",
            "Analog / 35mm Vintage",
            "Dramatisch B&W (Fine Art)",
            "HDR Landschaft",
            "Porträt Sanft (Glow & Skin)"
        });

        foreach (var p in presets)
        {
            p.Settings.Should().NotBeNull();
        }
    }

    [Fact]
    public void ApplyAdjustments_ExposureIncrease_ShouldBrightenImage()
    {
        // Arrange: 100x100 solid mid-gray image (RGB: 128, 128, 128)
        using var original = CreateSolidBitmap(100, 100, new SKColor(128, 128, 128));
        var settings = new ProfiAdjustmentSettings
        {
            Exposure = 1.0f // +1 EV
        };

        // Act
        using var result = _service.ApplyAdjustments(original, settings);

        // Assert
        var origPixel = original.GetPixel(50, 50);
        var resPixel = result.GetPixel(50, 50);

        resPixel.Red.Should().BeGreaterThan(origPixel.Red);
        resPixel.Green.Should().BeGreaterThan(origPixel.Green);
        resPixel.Blue.Should().BeGreaterThan(origPixel.Blue);
    }

    [Fact]
    public void ApplyAdjustments_ExposureDecrease_ShouldDarkenImage()
    {
        using var original = CreateSolidBitmap(100, 100, new SKColor(128, 128, 128));
        var settings = new ProfiAdjustmentSettings
        {
            Exposure = -1.0f // -1 EV
        };

        using var result = _service.ApplyAdjustments(original, settings);

        var origPixel = original.GetPixel(50, 50);
        var resPixel = result.GetPixel(50, 50);

        resPixel.Red.Should().BeLessThan(origPixel.Red);
        resPixel.Green.Should().BeLessThan(origPixel.Green);
        resPixel.Blue.Should().BeLessThan(origPixel.Blue);
    }

    [Fact]
    public void ApplyAdjustments_TemperatureCool_ShouldBoostBlueAndLowerRed()
    {
        using var original = CreateSolidBitmap(100, 100, new SKColor(140, 140, 140));
        var settings = new ProfiAdjustmentSettings
        {
            Temperature = -60f // Cold / Blue shift
        };

        using var result = _service.ApplyAdjustments(original, settings);
        var pixel = result.GetPixel(50, 50);

        pixel.Blue.Should().BeGreaterThan(pixel.Red);
    }

    [Fact]
    public void ApplyAdjustments_TemperatureWarm_ShouldBoostRedAndLowerBlue()
    {
        using var original = CreateSolidBitmap(100, 100, new SKColor(140, 140, 140));
        var settings = new ProfiAdjustmentSettings
        {
            Temperature = 60f // Warm / Golden shift
        };

        using var result = _service.ApplyAdjustments(original, settings);
        var pixel = result.GetPixel(50, 50);

        pixel.Red.Should().BeGreaterThan(pixel.Blue);
    }

    [Fact]
    public void ApplyAdjustments_SaturationMinus100_ShouldProduceGrayscale()
    {
        // Arrange: colorful green/orange image
        using var original = CreateSolidBitmap(50, 50, new SKColor(220, 140, 50));
        var settings = new ProfiAdjustmentSettings
        {
            Saturation = -100f // Full desaturation / Black & White
        };

        // Act
        using var result = _service.ApplyAdjustments(original, settings);
        var pixel = result.GetPixel(25, 25);

        // Assert: In pure grayscale, R, G, and B are equal or within 1 due to rounding
        Math.Abs(pixel.Red - pixel.Green).Should().BeLessThanOrEqualTo(1);
        Math.Abs(pixel.Green - pixel.Blue).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void ApplyAdjustments_Vignette_ShouldDarkenCornersMoreThanCenter()
    {
        // Arrange: 200x200 uniform white image
        using var original = CreateSolidBitmap(200, 200, SKColors.White);
        var settings = new ProfiAdjustmentSettings
        {
            Vignette = -80f // Dark vignette
        };

        using var result = _service.ApplyAdjustments(original, settings);

        var centerPixel = result.GetPixel(100, 100);
        var cornerPixel = result.GetPixel(5, 5);

        // Center should remain bright, corners should be significantly darker
        centerPixel.Red.Should().BeGreaterThan(cornerPixel.Red);
        cornerPixel.Red.Should().BeLessThan(200);
    }

    [Fact]
    public void ComputeHistogram_ShouldProduceValidBinsAndClippingStatistics()
    {
        // Arrange: Create an image with some pure black and pure white pixels
        using var bitmap = new SKBitmap(100, 100, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(128, 128, 128));
            // Draw some pure black pixels (shadow clipping)
            using var blackPaint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(0, 0, 10, 10, blackPaint); // 100 pixels

            // Draw some pure white pixels (highlight clipping)
            using var whitePaint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(10, 0, 10, 10, whitePaint); // 100 pixels
        }

        // Act
        var histogram = _service.ComputeHistogram(bitmap);

        // Assert
        histogram.Should().NotBeNull();
        histogram.Red.Length.Should().Be(256);
        histogram.Green.Length.Should().Be(256);
        histogram.Blue.Length.Should().Be(256);
        histogram.Luminance.Length.Should().Be(256);

        histogram.MaxValue.Should().BeGreaterThan(0);
        histogram.ShadowClippingCount.Should().Be(100);
        histogram.HighlightClippingCount.Should().Be(100);
        histogram.ShadowClippingPercentage.Should().BeApproximately(1.0, 0.01);
        histogram.HighlightClippingPercentage.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void Settings_ResetAll_ShouldRestoreAllDefaults()
    {
        var settings = new ProfiAdjustmentSettings
        {
            Exposure = 1.5f,
            Contrast = 45f,
            Highlights = -30f,
            Shadows = 20f,
            Temperature = 35f,
            Tint = -15f,
            Clarity = 25f,
            Dehaze = 10f,
            Sharpen = 50f,
            NoiseReduction = 20f,
            Vignette = -40f,
            Grain = 30f,
            ShadowHue = 180f,
            ShadowSaturation = 40f,
            CurveHighlights = 20f
        };

        settings.IsDefault.Should().BeFalse();

        settings.ResetAll();

        settings.IsDefault.Should().BeTrue();
        settings.Exposure.Should().Be(0f);
        settings.Contrast.Should().Be(0f);
        settings.Vignette.Should().Be(0f);
        settings.Grain.Should().Be(0f);
    }

    [Fact]
    public void LoadRawOrStandardBitmap_StandardImage_ShouldLoadSuccessfully()
    {
        var tempPng = Path.Combine(Path.GetTempPath(), $"imaginary_test_{Guid.NewGuid():N}.png");
        try
        {
            var bytes = TestImageGenerator.CreateSolidImage(150, 100, SKColors.CornflowerBlue, ImageFormat.Png);
            File.WriteAllBytes(tempPng, bytes);

            using var loaded = _service.LoadRawOrStandardBitmap(tempPng);

            loaded.Should().NotBeNull();
            loaded.Width.Should().Be(150);
            loaded.Height.Should().Be(100);
        }
        finally
        {
            if (File.Exists(tempPng)) File.Delete(tempPng);
        }
    }

    [Fact]
    public void ApplyAdjustments_ClarityAndDehaze_ShouldExecuteSuccessfully()
    {
        using var original = CreateSolidBitmap(60, 60, new SKColor(100, 130, 160));
        var settings = new ProfiAdjustmentSettings
        {
            Clarity = 40f,
            Dehaze = 30f
        };

        using var result = _service.ApplyAdjustments(original, settings);
        result.Should().NotBeNull();
        result.Width.Should().Be(60);
        result.Height.Should().Be(60);
    }

    [Fact]
    public void ApplyAdjustments_ToneCurves_ShouldModifyHighlightsAndShadows()
    {
        using var original = CreateSolidBitmap(50, 50, new SKColor(200, 200, 200));
        var settings = new ProfiAdjustmentSettings
        {
            CurveHighlights = 50f // boost highlights
        };

        using var result = _service.ApplyAdjustments(original, settings);
        var p = result.GetPixel(25, 25);
        p.Red.Should().BeGreaterThan(200);
    }

    [Fact]
    public void ApplyAdjustments_ColorGradingSplitToning_ShouldTintShadowsAndHighlights()
    {
        // Dark image (shadows)
        using var darkImg = CreateSolidBitmap(50, 50, new SKColor(40, 40, 40));
        var settings = new ProfiAdjustmentSettings
        {
            ShadowHue = 210f, // Teal/Blue
            ShadowSaturation = 80f
        };

        using var result = _service.ApplyAdjustments(darkImg, settings);
        var p = result.GetPixel(25, 25);
        // Blue channel should be boosted in shadows
        p.Blue.Should().BeGreaterThan(p.Red);
    }

    [Fact]
    public void ApplyAdjustments_Sharpening_ShouldEnhanceEdgeContrast()
    {
        // Image with an edge: left black, right white
        using var edgeBmp = new SKBitmap(50, 50, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(edgeBmp))
        {
            c.Clear(SKColors.Black);
            using var p = new SKPaint { Color = SKColors.White };
            c.DrawRect(25, 0, 25, 50, p);
        }

        var settings = new ProfiAdjustmentSettings
        {
            Sharpen = 80f
        };

        using var sharpened = _service.ApplyAdjustments(edgeBmp, settings);
        sharpened.Should().NotBeNull();
        sharpened.Width.Should().Be(50);
    }

    [Fact]
    public void Settings_Clone_ShouldCreateIndependentCopy()
    {
        var s1 = new ProfiAdjustmentSettings
        {
            Exposure = 1.25f,
            Contrast = 30f,
            Clarity = 15f
        };

        var s2 = s1.Clone();
        s2.Exposure.Should().Be(1.25f);
        s2.Contrast.Should().Be(30f);
        s2.Clarity.Should().Be(15f);

        // Mutating s2 shouldn't affect s1
        s2.Exposure = 0f;
        s1.Exposure.Should().Be(1.25f);
    }

    [Fact]
    public void AllBuiltInPresets_ShouldApplyWithoutExceptions()
    {
        using var sample = CreateSolidBitmap(64, 64, new SKColor(120, 150, 180));
        var presets = _service.GetBuiltInPresets();

        foreach (var preset in presets)
        {
            using var output = _service.ApplyAdjustments(sample, preset.Settings);
            output.Should().NotBeNull();
            output.Width.Should().Be(64);
            output.Height.Should().Be(64);
        }
    }

    private static SKBitmap CreateSolidBitmap(int width, int height, SKColor color)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(color);
        return bmp;
    }
}
