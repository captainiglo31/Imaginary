namespace Imaginary.Core.Models;

public class ImageJobInput
{
    public string? SourcePath { get; set; }
    public string FileName { get; set; } = string.Empty;
    public Stream? Stream { get; set; }
    public byte[]? Data { get; set; }
    public ImageFormat DetectedFormat { get; set; } = ImageFormat.Unknown;
    public long SourceSizeBytes { get; set; }

    public static ImageJobInput FromFile(string filePath)
    {
        var fi = new FileInfo(filePath);
        return new ImageJobInput
        {
            SourcePath = filePath,
            FileName = fi.Name,
            SourceSizeBytes = fi.Exists ? fi.Length : 0
        };
    }

    public static ImageJobInput FromStream(string fileName, Stream stream)
    {
        return new ImageJobInput
        {
            FileName = fileName,
            Stream = stream,
            SourceSizeBytes = stream.CanSeek ? stream.Length : 0
        };
    }

    public static ImageJobInput FromBytes(string fileName, byte[] data)
    {
        return new ImageJobInput
        {
            FileName = fileName,
            Data = data,
            SourceSizeBytes = data.Length
        };
    }
}
