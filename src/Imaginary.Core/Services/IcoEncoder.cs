using SkiaSharp;

namespace Imaginary.Core.Services;

public class IcoEncoder : IIcoEncoder
{
    private static readonly int[] DefaultSizes = { 16, 32, 48, 64, 128, 256 };

    public byte[] EncodeToIco(SKBitmap sourceBitmap, int[]? customSizes = null)
    {
        using var ms = new MemoryStream();
        SaveToIco(sourceBitmap, ms, customSizes);
        return ms.ToArray();
    }

    public void SaveToIco(SKBitmap sourceBitmap, Stream outputStream, int[]? customSizes = null)
    {
        if (sourceBitmap == null) throw new ArgumentNullException(nameof(sourceBitmap));
        if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));

        var sizes = (customSizes ?? DefaultSizes).OrderBy(s => s).Distinct().ToArray();
        var encodedImages = new List<(int Size, byte[] PngData)>();

        foreach (var size in sizes)
        {
            var pngBytes = RenderSizeToPng(sourceBitmap, size);
            encodedImages.Add((size, pngBytes));
        }

        using var bw = new BinaryWriter(outputStream, System.Text.Encoding.UTF8, leaveOpen: true);

        // 1. ICONDIR Header (6 bytes)
        bw.Write((ushort)0); // Reserved
        bw.Write((ushort)1); // Type: 1 = ICO
        bw.Write((ushort)encodedImages.Count); // Image count

        // 2. ICONDIRENTRY Array (16 bytes per entry)
        uint currentOffset = (uint)(6 + (16 * encodedImages.Count));

        foreach (var (size, pngData) in encodedImages)
        {
            byte dim = (byte)(size >= 256 ? 0 : size);
            bw.Write(dim); // Width (0 = 256px)
            bw.Write(dim); // Height (0 = 256px)
            bw.Write((byte)0); // Color count
            bw.Write((byte)0); // Reserved
            bw.Write((ushort)1); // Color planes
            bw.Write((ushort)32); // Bits per pixel
            bw.Write((uint)pngData.Length); // Size of image data
            bw.Write(currentOffset); // Offset from beginning of file

            currentOffset += (uint)pngData.Length;
        }

        // 3. Image Data
        foreach (var (_, pngData) in encodedImages)
        {
            bw.Write(pngData);
        }

        bw.Flush();
    }

    private static byte[] RenderSizeToPng(SKBitmap sourceBitmap, int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var target = new SKBitmap(info);
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        // Proportional scale & center within the square icon box
        float scale = Math.Min((float)size / sourceBitmap.Width, (float)size / sourceBitmap.Height);
        float destWidth = sourceBitmap.Width * scale;
        float destHeight = sourceBitmap.Height * scale;
        float destX = (size - destWidth) / 2f;
        float destY = (size - destHeight) / 2f;

        var destRect = new SKRect(destX, destY, destX + destWidth, destY + destHeight);
        canvas.DrawBitmap(sourceBitmap, destRect, paint);
        canvas.Flush();

        using var image = SKImage.FromBitmap(target);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
