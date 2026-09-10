using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class ImageConverter : IImageConverter
{
    private readonly IImageFormatDetector _formatDetector;
    private readonly IIcoEncoder _icoEncoder;

    public ImageConverter(IImageFormatDetector formatDetector, IIcoEncoder? icoEncoder = null)
    {
        _formatDetector = formatDetector ?? throw new ArgumentNullException(nameof(formatDetector));
        _icoEncoder = icoEncoder ?? new IcoEncoder();
    }

    public (SKBitmap Bitmap, ImageFormat DetectedFormat) LoadBitmap(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return LoadBitmap(ms.ToArray());
    }

    public (SKBitmap Bitmap, ImageFormat DetectedFormat) LoadBitmap(byte[] data)
    {
        var detectedFormat = _formatDetector.DetectFormat(data);

        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);

        if (codec == null)
        {
            // Fallback direct decode
            var fallbackBmp = SKBitmap.Decode(skData);
            if (fallbackBmp == null)
            {
                throw new InvalidOperationException("Failed to decode image data with SkiaSharp.");
            }
            return (fallbackBmp, detectedFormat);
        }

        var origin = codec.EncodedOrigin;
        var info = codec.Info;
        var bitmap = new SKBitmap(info);

        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            bitmap.Dispose();
            bitmap = SKBitmap.Decode(skData)
                ?? throw new InvalidOperationException($"Could not decode pixels from codec: {result}");
        }

        var orientedBitmap = ApplyOrientation(bitmap, origin);
        return (orientedBitmap, detectedFormat);
    }

    public (SKBitmap Bitmap, ImageFormat DetectedFormat) LoadBitmap(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        return LoadBitmap(bytes);
    }

    public byte[] Encode(SKBitmap bitmap, ImageFormat format, int quality = 90)
    {
        using var ms = new MemoryStream();
        EncodeToStream(bitmap, format, ms, quality);
        return ms.ToArray();
    }

    public void EncodeToStream(SKBitmap bitmap, ImageFormat format, Stream outputStream, int quality = 90)
    {
        quality = Math.Clamp(quality, 1, 100);

        if (format == ImageFormat.Ico)
        {
            _icoEncoder.SaveToIco(bitmap, outputStream);
            return;
        }

        if (format == ImageFormat.Bmp)
        {
            EncodeBmp(bitmap, outputStream);
            return;
        }

        var skFormat = ToSkEncodedImageFormat(format);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(skFormat, quality);

        if (data == null)
        {
            // Fallback for formats not natively encodable by Skia (e.g. GIF/TIFF -> PNG)
            using var fallbackData = image.Encode(SKEncodedImageFormat.Png, quality);
            if (fallbackData != null)
            {
                fallbackData.SaveTo(outputStream);
                return;
            }
            throw new InvalidOperationException($"SkiaSharp failed to encode image as {format} with quality {quality}.");
        }

        data.SaveTo(outputStream);
    }

    private static void EncodeBmp(SKBitmap bitmap, Stream stream)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rowPadding = (4 - (width * 3 % 4)) % 4;
        var imageSize = (width * 3 + rowPadding) * height;
        var fileSize = 54 + imageSize;

        using var writer = new BinaryWriter(stream, System.Text.Encoding.Default, leaveOpen: true);
        // BITMAPFILEHEADER
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0); // reserved
        writer.Write(54); // data offset

        // BITMAPINFOHEADER
        writer.Write(40); // header size
        writer.Write(width);
        writer.Write(height); // bottom-up
        writer.Write((short)1); // planes
        writer.Write((short)24); // bit count
        writer.Write(0); // compression BI_RGB
        writer.Write(imageSize);
        writer.Write(2835); // 72 DPI
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        // Pixel data: bottom-to-top, BGR
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                writer.Write(color.Blue);
                writer.Write(color.Green);
                writer.Write(color.Red);
            }
            for (var p = 0; p < rowPadding; p++)
            {
                writer.Write((byte)0);
            }
        }
    }

    public SKEncodedImageFormat ToSkEncodedImageFormat(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
        ImageFormat.Png => SKEncodedImageFormat.Png,
        ImageFormat.Webp => SKEncodedImageFormat.Webp,
        ImageFormat.Gif => SKEncodedImageFormat.Gif,
        ImageFormat.Bmp => SKEncodedImageFormat.Bmp,
        ImageFormat.Tiff => SKEncodedImageFormat.Png, // Fallback for encoding
        ImageFormat.Avif => SKEncodedImageFormat.Webp, // Fallback for encoding when libavif not compiled in Skia
        _ => SKEncodedImageFormat.Jpeg
    };

    private static SKBitmap ApplyOrientation(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft || origin == (SKEncodedOrigin)0)
        {
            return bitmap;
        }

        SKBitmap rotated;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // Flip horizontal
                rotated = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
                using (var canvas = new SKCanvas(rotated))
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.Scale(-1, 1, bitmap.Width / 2f, bitmap.Height / 2f);
                    canvas.DrawBitmap(bitmap, 0, 0);
                }
                bitmap.Dispose();
                return rotated;

            case SKEncodedOrigin.BottomRight: // 180 degrees
                rotated = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
                using (var canvas = new SKCanvas(rotated))
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.RotateDegrees(180, bitmap.Width / 2f, bitmap.Height / 2f);
                    canvas.DrawBitmap(bitmap, 0, 0);
                }
                bitmap.Dispose();
                return rotated;

            case SKEncodedOrigin.BottomLeft: // Flip vertical
                rotated = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
                using (var canvas = new SKCanvas(rotated))
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.Scale(1, -1, bitmap.Width / 2f, bitmap.Height / 2f);
                    canvas.DrawBitmap(bitmap, 0, 0);
                }
                bitmap.Dispose();
                return rotated;

            case SKEncodedOrigin.RightTop: // Rotate 90 CW
                rotated = new SKBitmap(bitmap.Height, bitmap.Width, bitmap.ColorType, bitmap.AlphaType);
                using (var canvas = new SKCanvas(rotated))
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.Translate(bitmap.Height, 0);
                    canvas.RotateDegrees(90);
                    canvas.DrawBitmap(bitmap, 0, 0);
                }
                bitmap.Dispose();
                return rotated;

            case SKEncodedOrigin.LeftBottom: // Rotate 270 CW
                rotated = new SKBitmap(bitmap.Height, bitmap.Width, bitmap.ColorType, bitmap.AlphaType);
                using (var canvas = new SKCanvas(rotated))
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.Translate(0, bitmap.Width);
                    canvas.RotateDegrees(270);
                    canvas.DrawBitmap(bitmap, 0, 0);
                }
                bitmap.Dispose();
                return rotated;

            default:
                return bitmap;
        }
    }
}
