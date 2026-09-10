using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using Xunit;

namespace Imaginary.Core.Tests;

public class OutputPathResolverTests
{
    private readonly OutputPathResolver _resolver = new(new ImageFormatDetector());

    [Fact]
    public void ResolveOutputPath_ShouldChangeExtensionToTargetFormat()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_test_{Guid.NewGuid():N}");
        try
        {
            var result = _resolver.ResolveOutputPath(null, "photo.png", ImageFormat.Jpeg, tempDir);

            Path.GetExtension(result).Should().Be(".jpg");
            Path.GetFileNameWithoutExtension(result).Should().Be("photo");
            Path.GetDirectoryName(result).Should().Be(tempDir);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResolveOutputPath_WhenTargetAlreadyExists_ShouldAppendNumericSuffix()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var existingFile = Path.Combine(tempDir, "sample.webp");
            File.WriteAllText(existingFile, "placeholder");

            var result = _resolver.ResolveOutputPath(null, "sample.jpg", ImageFormat.Webp, tempDir);

            Path.GetFileName(result).Should().Be("sample_1.webp");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResolveOutputPath_WhenOutputWouldOverwriteOriginal_ShouldAppendSuffix()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var originalSource = Path.Combine(tempDir, "original.jpg");
            File.WriteAllText(originalSource, "original content");

            // Output dir is same as source dir, target format is same as original
            var result = _resolver.ResolveOutputPath(originalSource, "original.jpg", ImageFormat.Jpeg, tempDir);

            // Must NOT equal the original file path!
            result.Should().NotBe(originalSource);
            Path.GetFileName(result).Should().Be("original_1.jpg");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
