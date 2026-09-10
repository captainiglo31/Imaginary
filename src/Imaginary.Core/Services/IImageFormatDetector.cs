using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public interface IImageFormatDetector
{
    ImageFormat DetectFormat(ReadOnlySpan<byte> headerBytes);
    ImageFormat DetectFormat(Stream stream);
    ImageFormat DetectFormat(string filePath);
    ImageFormat DetectFromExtension(string extensionOrFileName);
    string GetDefaultExtension(ImageFormat format);
    string GetMimeType(ImageFormat format);
    bool IsLossy(ImageFormat format);
}
