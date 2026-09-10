using FluentAssertions;
using Imaginary.Core.Services;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class IcoEncoderTests
{
    private readonly IcoEncoder _encoder = new();

    [Fact]
    public void EncodeToIco_ShouldProduceValidIcoHeaderAndEntries()
    {
        // Arrange: 512x512 bitmap
        using var bitmap = new SKBitmap(512, 512);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawCircle(256, 256, 128, paint);
        }

        // Act: Generate standard multi-res ICO
        var icoBytes = _encoder.EncodeToIco(bitmap);

        // Assert
        icoBytes.Should().NotBeNull();
        icoBytes.Length.Should().BeGreaterThan(6 + 16 * 6); // Header + 6 entries + image data

        using var ms = new MemoryStream(icoBytes);
        using var reader = new BinaryReader(ms);

        var reserved = reader.ReadUInt16();
        var type = reader.ReadUInt16();
        var count = reader.ReadUInt16();

        reserved.Should().Be(0);
        type.Should().Be(1); // 1 = ICO
        count.Should().Be(6); // Default 6 sizes (16, 32, 48, 64, 128, 256)

        // Read entries
        for (int i = 0; i < count; i++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            var colorCount = reader.ReadByte();
            var res = reader.ReadByte();
            var planes = reader.ReadUInt16();
            var bitCount = reader.ReadUInt16();
            var size = reader.ReadUInt32();
            var offset = reader.ReadUInt32();

            planes.Should().Be(1);
            bitCount.Should().Be(32);
            size.Should().BeGreaterThan(0);
            offset.Should().BeGreaterThanOrEqualTo(6 + 16 * 6);
        }
    }

    [Fact]
    public void GenerateAssetsFromLogo()
    {
        var logoSrc = @"C:\Users\pino.wackers\.gemini\antigravity\brain\1c275442-fff7-463f-a112-839d73756128\.user_uploaded\media_1789036670443.png";
        if (!File.Exists(logoSrc)) return;

        using var bitmap = SKBitmap.Decode(logoSrc);
        bitmap.Should().NotBeNull();

        using var image = SKImage.FromBitmap(bitmap);
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
        var pngBytes = pngData.ToArray();

        var icoBytes = _encoder.EncodeToIco(bitmap);

        var repoRoot = @"C:\git\Imaginary";
        var desktopAssets = Path.Combine(repoRoot, @"src\Imaginary.Desktop\Assets");
        var webAssets = Path.Combine(repoRoot, @"src\Imaginary.Web\wwwroot\images");
        var portableAssets = Path.Combine(repoRoot, "portable-assets");

        Directory.CreateDirectory(desktopAssets);
        Directory.CreateDirectory(webAssets);
        Directory.CreateDirectory(portableAssets);

        File.WriteAllBytes(Path.Combine(desktopAssets, "logo.png"), pngBytes);
        File.WriteAllBytes(Path.Combine(desktopAssets, "app.ico"), icoBytes);
        File.WriteAllBytes(Path.Combine(repoRoot, @"src\Imaginary.Desktop\app.ico"), icoBytes);

        File.WriteAllBytes(Path.Combine(webAssets, "logo.png"), pngBytes);
        File.WriteAllBytes(Path.Combine(repoRoot, @"src\Imaginary.Web\wwwroot\favicon.ico"), icoBytes);

        File.WriteAllBytes(Path.Combine(portableAssets, "logo.png"), pngBytes);
        File.WriteAllBytes(Path.Combine(portableAssets, "app.ico"), icoBytes);
    }
}
