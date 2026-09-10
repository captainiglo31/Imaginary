using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public class ImageFormatDetector : IImageFormatDetector
{
    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] Gif87aMagic = { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 }; // GIF87a
    private static readonly byte[] Gif89aMagic = { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }; // GIF89a
    private static readonly byte[] BmpMagic = { 0x42, 0x4D }; // BM
    private static readonly byte[] RiffMagic = { 0x52, 0x49, 0x46, 0x46 }; // RIFF
    private static readonly byte[] WebpMagic = { 0x57, 0x45, 0x42, 0x50 }; // WEBP
    private static readonly byte[] TiffLeMagic = { 0x49, 0x49, 0x2A, 0x00 }; // II*\0
    private static readonly byte[] TiffBeMagic = { 0x4D, 0x4D, 0x00, 0x2A }; // MM\0*

    public ImageFormat DetectFormat(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length >= 3 && headerBytes[..3].SequenceEqual(JpegMagic))
        {
            return ImageFormat.Jpeg;
        }

        if (headerBytes.Length >= 8 && headerBytes[..8].SequenceEqual(PngMagic))
        {
            return ImageFormat.Png;
        }

        if (headerBytes.Length >= 6 &&
            (headerBytes[..6].SequenceEqual(Gif87aMagic) || headerBytes[..6].SequenceEqual(Gif89aMagic)))
        {
            return ImageFormat.Gif;
        }

        if (headerBytes.Length >= 2 && headerBytes[..2].SequenceEqual(BmpMagic))
        {
            return ImageFormat.Bmp;
        }

        if (headerBytes.Length >= 12 &&
            headerBytes[..4].SequenceEqual(RiffMagic) &&
            headerBytes.Slice(8, 4).SequenceEqual(WebpMagic))
        {
            return ImageFormat.Webp;
        }

        if (headerBytes.Length >= 4 &&
            (headerBytes[..4].SequenceEqual(TiffLeMagic) || headerBytes[..4].SequenceEqual(TiffBeMagic)))
        {
            return ImageFormat.Tiff;
        }

        // AVIF: ISOBMFF ftyp header with avif / avis brand
        if (headerBytes.Length >= 12 &&
            headerBytes.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = headerBytes.Slice(8, 4);
            if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8) || brand.SequenceEqual("mif1"u8))
            {
                return ImageFormat.Avif;
            }
        }

        // ICO: 00 00 01 00
        if (headerBytes.Length >= 4 &&
            headerBytes[0] == 0x00 && headerBytes[1] == 0x00 &&
            headerBytes[2] == 0x01 && headerBytes[3] == 0x00)
        {
            return ImageFormat.Ico;
        }

        return ImageFormat.Unknown;
    }

    public ImageFormat DetectFormat(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[32];
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        var bytesRead = stream.Read(buffer);

        if (stream.CanSeek)
        {
            stream.Position = originalPosition;
        }

        if (bytesRead < 2)
        {
            return ImageFormat.Unknown;
        }

        return DetectFormat(buffer[..bytesRead]);
    }

    public ImageFormat DetectFormat(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return DetectFromExtension(filePath);
        }

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var detected = DetectFormat(fs);
        return detected != ImageFormat.Unknown ? detected : DetectFromExtension(filePath);
    }

    public ImageFormat DetectFromExtension(string extensionOrFileName)
    {
        var ext = Path.GetExtension(extensionOrFileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".jpe" or ".jfif" => ImageFormat.Jpeg,
            ".png" => ImageFormat.Png,
            ".gif" => ImageFormat.Gif,
            ".bmp" or ".dib" => ImageFormat.Bmp,
            ".webp" => ImageFormat.Webp,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            ".avif" => ImageFormat.Avif,
            ".ico" => ImageFormat.Ico,
            _ => ImageFormat.Unknown
        };
    }

    public string GetDefaultExtension(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Png => ".png",
        ImageFormat.Gif => ".gif",
        ImageFormat.Bmp => ".bmp",
        ImageFormat.Webp => ".webp",
        ImageFormat.Tiff => ".tiff",
        ImageFormat.Avif => ".avif",
        ImageFormat.Ico => ".ico",
        _ => ".bin"
    };

    public string GetMimeType(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Png => "image/png",
        ImageFormat.Gif => "image/gif",
        ImageFormat.Bmp => "image/bmp",
        ImageFormat.Webp => "image/webp",
        ImageFormat.Tiff => "image/tiff",
        ImageFormat.Avif => "image/avif",
        ImageFormat.Ico => "image/x-icon",
        _ => "application/octet-stream"
    };

    public bool IsLossy(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg or ImageFormat.Webp or ImageFormat.Avif => true,
        _ => false
    };
}
